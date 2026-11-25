using System.Text.Json;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Contracts.Messaging;

namespace soft.square.net.WebApi.Tests.Messaging;

public sealed class IntegrationTypesTests
{
    [Theory]
    [InlineData("org.unit-*.v1", "org.unit-created.v1", true)]
    [InlineData("org.unit-*.v1", "org.unit-moved.v1", true)]
    [InlineData("org.unit-*.v1", "org.membership-changed.v1", false)]
    [InlineData("org.unit-*.v1", "org.unit-created.v2", false)]
    [InlineData("shifts.*.v1", "shifts.checked-in.v1", true)]
    [InlineData("shifts.*.v1", "shifts.checked-in.extra.v1", false)]
    [InlineData("org.#", "org.unit-created.v1", true)]
    [InlineData("org.#", "org", true)]
    [InlineData("#.v1", "content.post-published.v1", true)]
    [InlineData("#", "anything.at.all", true)]
    [InlineData("content.post-published.v1", "content.post-published.v1", true)]
    [InlineData("content.post-published.v1", "content.post-published.v10", false)]
    [InlineData("a*b*c.v1", "axxbyyc.v1", true)]
    [InlineData("a*b*c.v1", "axxbyy.v1", false)]
    [InlineData("*-in.v1", "checked-in.v1", true)]
    public void Patterns_match_types(string pattern, string type, bool expected) =>
        IntegrationTypes.Matches(pattern, type).ShouldBe(expected);

    [Theory]
    [InlineData("org.unit-*.v1", "org.*.v1")]
    [InlineData("shifts.*.v1", "shifts.*.v1")]
    [InlineData("org.#", "org.#")]
    [InlineData("content.post-published.v1", "content.post-published.v1")]
    public void Binding_keys_are_whole_word_wildcards(string pattern, string key) =>
        IntegrationTypes.ToBindingKey(pattern).ShouldBe(key);

    [Theory]
    [InlineData("shifts.checked-in.v1", true)]
    [InlineData("Shifts.CheckedIn.v1", false)]
    [InlineData("shifts..v1", false)]
    [InlineData("shifts.*.v1", false)]
    public void Types_are_validated(string type, bool valid) => IntegrationTypes.IsValidType(type).ShouldBe(valid);

    [Theory]
    [InlineData("org.unit-*.v1", true)]
    [InlineData("org.#", true)]
    [InlineData("org.#x", false)]
    [InlineData("", false)]
    [InlineData("Org.*", false)]
    public void Patterns_are_validated(string pattern, bool valid) => IntegrationTypes.IsValidPattern(pattern).ShouldBe(valid);

    [Fact]
    public void Envelope_round_trips_with_its_data()
    {
        var data = JsonSerializer.SerializeToElement(new { unitId = "u1", name = "Desk" }, IntegrationEnvelope.JsonOptions);
        var envelope = new IntegrationEnvelope(Guid.NewGuid(), "org.unit-created.v1", "acme", "unit:u1", DateTimeOffset.UtcNow,
            "corr", "organization", data, OrgId: "org-1", UnitPath: "/org-1/u1");

        var parsed = IntegrationEnvelope.Parse(envelope.ToUtf8Bytes());

        parsed.Id.ShouldBe(envelope.Id);
        parsed.Type.ShouldBe(envelope.Type);
        parsed.TenantId.ShouldBe("acme");
        parsed.UnitPath.ShouldBe("/org-1/u1");
        parsed.OrgId.ShouldBe("org-1");
        parsed.OccurredAt.ShouldBe(envelope.OccurredAt);
        parsed.DataAs<UnitCreated>().ShouldBe(new UnitCreated("u1", "Desk"));
        System.Text.Encoding.UTF8.GetString(envelope.ToUtf8Bytes()).ShouldContain("\"tenantId\":\"acme\"");
    }

    [Fact]
    public void Queue_names_follow_module_and_pool()
    {
        IntegrationNames.Queue("shifts", "core").ShouldBe("integration.shifts.core");
        IntegrationNames.RetryQueue("integration.shifts.core").ShouldBe("integration.shifts.core.retry");
        IntegrationNames.DeadQueue("integration.shifts.core").ShouldBe("integration.shifts.core.dead");
    }

    [Fact]
    public void Manifest_subscriptions_default_to_none()
    {
        new ModuleManifest("a", "A", "1.0.0", "a").Subscriptions.ShouldBeEmpty();
        new ModuleManifest("a", "A", "1.0.0", "a") { Subscriptions = ["org.#"] }.Subscriptions.ShouldBe(["org.#"]);
    }

    private sealed record UnitCreated(string UnitId, string Name);
}
