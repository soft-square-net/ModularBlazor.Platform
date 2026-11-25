using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Live;

namespace soft.square.net.WebApi.Tests.Live;

/// <summary>The live hub on one replica without a broker: authorization, tenant isolation, unit fan-out, revocation.</summary>
public sealed class LiveHubTests : IAsyncLifetime
{
    private LiveTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await LiveTestHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task Connecting_without_a_token_is_refused()
    {
        var hub = new Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(_host.Url), LiveHubMethods.Path), o =>
            {
                o.SkipNegotiation = true;
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
            })
            .Build();
        await Should.ThrowAsync<Exception>(() => hub.StartAsync());
    }

    [Fact]
    public async Task A_user_topic_is_the_users_own_only()
    {
        await using var alice = await _host.ConnectAsync("alice");
        (await alice.SubscribeAsync("user:alice")).Ok.ShouldBeTrue();
        (await alice.SubscribeAsync("user:bob")).ShouldBe(new LiveSubscribeResult(false, "forbidden"));
    }

    [Fact]
    public async Task A_module_topic_needs_its_declared_permission_for_the_resource()
    {
        await using var alice = await _host.ConnectAsync("alice");
        (await alice.SubscribeAsync("content:post:42")).ShouldBe(new LiveSubscribeResult(false, "forbidden"));

        await _host.GrantAsync("acme", "alice", "content.view", "post:42");
        (await alice.SubscribeAsync("content:post:42")).Ok.ShouldBeTrue();
        (await alice.SubscribeAsync("content:post:43")).ShouldBe(new LiveSubscribeResult(false, "forbidden"));
    }

    [Fact]
    public async Task A_module_topic_counts_the_scopes_its_module_resolves_for_the_resource()
    {
        await using var alice = await _host.ConnectAsync("alice");
        await _host.GrantAsync("acme", "alice", "content.view", "press:A");

        (await alice.SubscribeAsync("content:post:42")).Ok.ShouldBeTrue();  // post 42 is in press A (the module says so)
        (await alice.SubscribeAsync("content:post:7")).Error.ShouldBe("forbidden"); // post 7 is not
        (await alice.SubscribeAsync("content:draft:42")).Error.ShouldBe("forbidden"); // drafts need content.edit
    }

    [Fact]
    public async Task Bypass_a_module_topic_cannot_be_opened_with_a_scope_the_client_sends()
    {
        await using var alice = await _host.ConnectAsync("alice");
        await _host.GrantAsync("acme", "alice", "content.view", "press:A");

        // The old (scopes) call shape is not a hub method any more...
        await Should.ThrowAsync<Exception>(() => alice.InvokeSubscribeAsync("content:post:7", new[] { "press:A" }));
        // ...and the topic stays closed: post 7 is not in press A on the server's word.
        (await alice.SubscribeAsync("content:post:7")).Error.ShouldBe("forbidden");
        await _host.PublishAsync("acme", n => n.PublishAsync("content:post:7", "post.updated"));
        (await alice.QuietAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_and_malformed_topics_are_refused()
    {
        await using var alice = await _host.ConnectAsync("alice", "acme");
        await _host.GrantAsync("acme", "alice", "content.view", "tenant");
        (await alice.SubscribeAsync("content:comment:1")).Error.ShouldBe("unknown-topic");
        (await alice.SubscribeAsync("shop:order:1")).Error.ShouldBe("unknown-topic");
        (await alice.SubscribeAsync("org:unit:desk")).Error.ShouldBe("unknown-topic"); // the unit kind is the platform's unit: topic
        (await alice.SubscribeAsync("content")).Error.ShouldBe("invalid-topic");
        (await alice.SubscribeAsync("content:post")).Error.ShouldBe("invalid-topic");
        (await alice.SubscribeAsync("Content:post:1")).Error.ShouldBe("invalid-topic");
        (await alice.SubscribeAsync("content:post:1")).Ok.ShouldBeTrue(); // tenant-wide grant
    }

    [Fact]
    public async Task A_unit_topic_needs_the_declared_permission_in_the_unit_or_a_server_resolved_ancestor()
    {
        await using var alice = await _host.ConnectAsync("alice");
        await _host.GrantAsync("acme", "alice", "org.units.view", "unit:region");

        (await alice.SubscribeAsync("unit:desk")).Ok.ShouldBeTrue();   // desk is below region
        (await alice.SubscribeAsync("unit:region")).Ok.ShouldBeTrue();
        (await alice.SubscribeAsync("unit:root")).Error.ShouldBe("forbidden"); // rights flow down, not up
        (await alice.SubscribeAsync("unit:other")).Error.ShouldBe("forbidden");
    }

    [Fact]
    public async Task Bypass_a_unit_topic_cannot_be_opened_with_a_foreign_scope_or_an_unrelated_permission()
    {
        await using var alice = await _host.ConnectAsync("alice");
        await _host.GrantAsync("acme", "alice", "org.units.view", "unit:A");
        await _host.GrantAsync("acme", "alice", "shifts.view", "unit:B"); // a right in unit B, but not the unit topic's

        await Should.ThrowAsync<Exception>(() => alice.InvokeSubscribeAsync("unit:B", new[] { "unit:A" }));
        (await alice.SubscribeAsync("unit:B")).Error.ShouldBe("forbidden");
        (await alice.SubscribeAsync("unit:A")).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Unit_topics_are_refused_when_no_module_provides_them()
    {
        await using var host = await LiveTestHost.StartAsync(unitProvider: false);
        await using var alice = await host.ConnectAsync("alice");
        await host.GrantAsync("acme", "alice", "org.units.view", "tenant");
        (await alice.SubscribeAsync("unit:desk")).Error.ShouldBe("unknown-topic");
    }

    [Fact]
    public async Task A_platform_role_alone_does_not_open_unit_topics()
    {
        // Platform roles hold platform permissions only; the unit topic needs the declared module permission.
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(_host.Url), LiveHubMethods.Path), o =>
            {
                o.SkipNegotiation = true;
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                o.AccessTokenProvider = () => Task.FromResult<string?>(LiveTestHost.Token("root", "acme", "platform-admin"));
            })
            .Build();
        await using var admin = new LiveTestClient(hub);
        await hub.StartAsync();
        (await admin.SubscribeAsync("unit:desk")).Error.ShouldBe("forbidden");
    }

    [Fact]
    public async Task Messages_reach_the_topic_in_the_publishers_tenant_only()
    {
        await using var alice = await _host.ConnectAsync("alice", "acme");
        await using var gina = await _host.ConnectAsync("gina", "globex");
        await _host.GrantAsync("acme", "alice", "content.view", "tenant");
        await _host.GrantAsync("globex", "gina", "content.view", "tenant");
        (await alice.SubscribeAsync("content:post:42")).Ok.ShouldBeTrue();
        (await gina.SubscribeAsync("content:post:42")).Ok.ShouldBeTrue();

        await _host.PublishAsync("acme", n => n.PublishAsync("content:post:42", "post.updated", Json(new { version = 7 }), sequence: 7));

        var message = await alice.NextAsync();
        message.Topic.ShouldBe("content:post:42");
        message.Type.ShouldBe("post.updated");
        message.Sequence.ShouldBe(7);
        message.Payload!.Value.GetProperty("version").GetInt32().ShouldBe(7);
        (await gina.QuietAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_unit_message_fans_out_to_every_ancestor_without_their_sequence()
    {
        await using var desk = await _host.ConnectAsync("desk-editor");
        await using var chief = await _host.ConnectAsync("chief");
        await _host.GrantAsync("acme", "desk-editor", "org.units.view", "unit:desk");
        await _host.GrantAsync("acme", "chief", "org.units.view", "unit:root");
        (await desk.SubscribeAsync("unit:desk")).Ok.ShouldBeTrue();
        (await chief.SubscribeAsync("unit:root")).Ok.ShouldBeTrue();

        await _host.PublishAsync("acme", n => n.PublishToUnitAsync("root/region/desk", "shift.checked-in", sequence: 12));

        var own = await desk.NextAsync();
        own.ShouldSatisfyAllConditions(() => own.Topic.ShouldBe("unit:desk"), () => own.Sequence.ShouldBe(12));
        var above = await chief.NextAsync();
        above.ShouldSatisfyAllConditions(() => above.Topic.ShouldBe("unit:root"), () => above.Sequence.ShouldBeNull());
    }

    [Fact]
    public async Task Unsubscribe_stops_the_messages()
    {
        await using var alice = await _host.ConnectAsync("alice");
        (await alice.SubscribeAsync("user:alice")).Ok.ShouldBeTrue();
        await _host.PublishAsync("acme", n => n.PublishAsync("user:alice", "note"));
        (await alice.NextAsync()).Type.ShouldBe("note");

        await alice.UnsubscribeAsync("user:alice");
        await _host.PublishAsync("acme", n => n.PublishAsync("user:alice", "note"));
        (await alice.QuietAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_grant_change_drops_the_topics_the_user_may_no_longer_receive()
    {
        await using var alice = await _host.ConnectAsync("alice");
        await _host.GrantAsync("acme", "alice", "content.view", "post:42");
        (await alice.SubscribeAsync("content:post:42")).Ok.ShouldBeTrue();
        (await alice.SubscribeAsync("user:alice")).Ok.ShouldBeTrue();

        await _host.RevokeAllAsync("acme", "alice"); // check-out: the shift's grants end
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (await alice.Revoked.Reader.ReadAsync(timeout.Token)).ShouldBe("content:post:42");

        await _host.PublishAsync("acme", n => n.PublishAsync("content:post:42", "post.updated"));
        await _host.PublishAsync("acme", n => n.PublishAsync("user:alice", "still-mine"));
        (await alice.NextAsync()).Type.ShouldBe("still-mine"); // the user topic needs no grant
        (await alice.QuietAsync()).ShouldBeTrue();
    }

    [Fact]
    public void Topic_names()
    {
        LiveTopics.UnitAndAncestors("root/region/desk").ShouldBe(["unit:desk", "unit:region", "unit:root"]);
        LiveTopics.UnitAndAncestors("/root/").ShouldBe(["unit:root"]);
        LiveTopics.Of("content", "post", "42").ShouldBe("content:post:42");
        LiveTopicAuthorizer.TryParse("content:post:a:b", out var kind, out var rest).ShouldBeTrue();
        (kind, rest).ShouldBe(("content", "post:a:b"));
        LiveTopicAuthorizer.TryParse("content:post: x", out _, out _).ShouldBeFalse();
        LiveTopicAuthorizer.TryParse(new string('a', 300) + ":x", out _, out _).ShouldBeFalse();
    }
}
