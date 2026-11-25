using System.Text.Json;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.WorkItems;

namespace soft.square.net.WebApi.Tests.WorkItems;

/// <summary>The shared work-item contract: references, assignees, snapshots and the event types.</summary>
public sealed class WorkItemContractTests
{
    private static readonly WorkItemSnapshot Item = new(
        new WorkItemRef("content", "content", "42"), "Match report", "InProgress", WorkItemAssignee.Shift("s-7"),
        new DateTimeOffset(2026, 11, 25, 18, 0, 0, TimeSpan.Zero), "desk-sports");

    [Fact]
    public void A_reference_round_trips_through_its_text()
    {
        var reference = new WorkItemRef("newsroom", "post-project", "7");
        reference.ToString().ShouldBe("newsroom:post-project:7");
        WorkItemRef.Parse("newsroom:post-project:7").ShouldBe(reference);
        WorkItemRef.Parse("content.designer:item:a:b").ShouldBe(new WorkItemRef("content.designer", "item", "a:b"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("content:42")]
    [InlineData("Content:content:42")]
    [InlineData("content::42")]
    [InlineData("content:content:")]
    public void Malformed_references_do_not_parse(string? text)
    {
        WorkItemRef.TryParse(text, out _).ShouldBeFalse();
        if (text is not null) Should.Throw<FormatException>(() => WorkItemRef.Parse(text));
    }

    [Fact]
    public void Assignees_have_a_scope_like_text()
    {
        WorkItemAssignee.User("u1").ToString().ShouldBe("user:u1");
        WorkItemAssignee.Shift("s1").ToString().ShouldBe("shift:s1");
        WorkItemAssignee.Role("editor").ToString().ShouldBe("role:editor");
        WorkItemAssignee.Unit("desk").ToString().ShouldBe("unit:desk");
    }

    [Fact]
    public void Event_types_are_per_module_and_versioned()
    {
        WorkItemEvents.Assigned("content").ShouldBe("content.work-item-assigned.v1");
        WorkItemEvents.StateChanged("newsroom").ShouldBe("newsroom.work-item-state-changed.v1");
        IntegrationTypes.IsValidType(WorkItemEvents.Assigned("content.designer")).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => WorkItemEvents.Assigned("Content"));
        Should.Throw<ArgumentException>(() => WorkItemEvents.Assigned(""));
    }

    [Fact]
    public void The_any_module_patterns_match_every_module_and_nothing_else()
    {
        IntegrationTypes.IsValidPattern(WorkItemEvents.AnyAssigned).ShouldBeTrue();
        IntegrationTypes.IsValidPattern(WorkItemEvents.AnyStateChanged).ShouldBeTrue();

        IntegrationTypes.Matches(WorkItemEvents.AnyAssigned, "content.work-item-assigned.v1").ShouldBeTrue();
        IntegrationTypes.Matches(WorkItemEvents.AnyAssigned, "content.designer.work-item-assigned.v1").ShouldBeTrue();
        IntegrationTypes.Matches(WorkItemEvents.AnyAssigned, "content.work-item-state-changed.v1").ShouldBeFalse();
        IntegrationTypes.Matches(WorkItemEvents.AnyAssigned, "content.work-item-assigned.v2").ShouldBeFalse();
        IntegrationTypes.Matches(WorkItemEvents.AnyStateChanged, "newsroom.work-item-state-changed.v1").ShouldBeTrue();
    }

    [Theory]
    [InlineData("content.work-item-assigned.v1", "content")]
    [InlineData("content.designer.work-item-state-changed.v1", "content.designer")]
    [InlineData("content.post-created.v1", null)]
    [InlineData("work-item-assigned.v1", null)]
    [InlineData("content.work-item-assigned.v2", null)]
    public void The_module_of_an_event_type(string type, string? module)
    {
        WorkItemEvents.ModuleOf(type).ShouldBe(module);
        (WorkItemEvents.IsAssigned(type) || WorkItemEvents.IsStateChanged(type)).ShouldBe(module is not null);
    }

    [Fact]
    public void A_snapshot_serializes_with_lowercase_assignee_kinds_and_round_trips()
    {
        var json = JsonSerializer.Serialize(new WorkItemAssigned(Item, WorkItemAssignee.User("u1")), IntegrationEnvelope.JsonOptions);

        using var document = JsonDocument.Parse(json);
        var item = document.RootElement.GetProperty("item");
        item.GetProperty("ref").GetProperty("module").GetString().ShouldBe("content");
        item.GetProperty("assignee").GetProperty("kind").GetString().ShouldBe("shift");
        document.RootElement.GetProperty("previousAssignee").GetProperty("kind").GetString().ShouldBe("user");

        var back = JsonSerializer.Deserialize<WorkItemAssigned>(json, IntegrationEnvelope.JsonOptions)!;
        back.Item.ShouldBe(Item);
        back.PreviousAssignee.ShouldBe(WorkItemAssignee.User("u1"));
    }

    [Fact]
    public void Publishing_uses_the_owner_module_type_and_the_reference_as_subject()
    {
        var events = new RecordingEvents();

        events.PublishWorkItemAssigned(Item, previousAssignee: null, unitPath: "root/sports/desk-sports");
        events.PublishWorkItemStateChanged(Item, previousState: "Assigned");

        events.Published[0].Type.ShouldBe("content.work-item-assigned.v1");
        events.Published[0].Subject.ShouldBe("content:content:42");
        events.Published[0].UnitPath.ShouldBe("root/sports/desk-sports");
        events.Published[0].DataAs<WorkItemAssigned>().Item.ShouldBe(Item);
        events.Published[1].Type.ShouldBe("content.work-item-state-changed.v1");
        events.Published[1].DataAs<WorkItemStateChanged>().PreviousState.ShouldBe("Assigned");
    }

    private sealed class RecordingEvents : IIntegrationEvents
    {
        public List<IntegrationEnvelope> Published { get; } = [];

        public IntegrationEnvelope Publish(string type, object data, string subject, string? unitPath = null, string? orgId = null)
        {
            var envelope = new IntegrationEnvelope(Guid.NewGuid(), type, "acme", subject, DateTimeOffset.UtcNow, "corr", "content",
                JsonSerializer.SerializeToElement(data, IntegrationEnvelope.JsonOptions), orgId, unitPath);
            Published.Add(envelope);
            return envelope;
        }
    }
}
