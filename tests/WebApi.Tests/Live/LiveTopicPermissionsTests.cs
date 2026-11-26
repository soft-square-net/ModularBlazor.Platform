using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Live;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Tests.Live;

/// <summary>Live topics with several permissions (<see cref="LiveTopic.AnyOf"/>): the contract, install checks and the hub.</summary>
public sealed class LiveTopicPermissionsTests : IAsyncLifetime
{
    private LiveTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await LiveTestHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    // --- The contract ---

    [Fact]
    public void A_single_permission_topic_is_unchanged()
    {
        var topic = new LiveTopic("post", "content.view");

        topic.Permission.ShouldBe("content.view");
        topic.OrPermissions.ShouldBeEmpty();
        topic.Permissions.ShouldBe(["content.view"]);
    }

    [Fact]
    public void AnyOf_keeps_the_first_permission_as_Permission_and_drops_repeats()
    {
        var topic = LiveTopic.AnyOf("post", "content.author", "content.accept", "content.author", "content.publish");

        topic.Permission.ShouldBe("content.author");
        topic.OrPermissions.ShouldBe(["content.accept", "content.publish"]);
        topic.Permissions.ShouldBe(["content.author", "content.accept", "content.publish"]);
        topic.ShouldBe(LiveTopic.AnyOf("post", "content.author", "content.accept", "content.publish"));
        topic.ShouldNotBe(new LiveTopic("post", "content.author"));
        Should.Throw<ArgumentException>(() => LiveTopic.AnyOf("post"));
    }

    [Fact]
    public void Records_written_before_AnyOf_still_read_and_new_ones_round_trip()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var old = JsonSerializer.Deserialize<LiveTopic>("""{"kind":"post","permission":"content.author"}""", web)!;
        old.Permissions.ShouldBe(["content.author"]);

        var json = JsonSerializer.Serialize(LiveTopic.AnyOf("post", "content.author", "content.accept"), web);
        json.ShouldBe("""{"kind":"post","permission":"content.author","orPermissions":["content.accept"]}""");
        JsonSerializer.Deserialize<LiveTopic>(json, web)!.Permissions.ShouldBe(["content.author", "content.accept"]);

        // A damaged record (null list) never throws and adds nothing.
        JsonSerializer.Deserialize<LiveTopic>("""{"kind":"post","permission":"a","orPermissions":null}""", web)!.Permissions.ShouldBe(["a"]);
    }

    // --- Install checks ---

    [Fact]
    public void A_kind_declared_twice_is_refused_at_install()
    {
        var e = Should.Throw<ModuleException>(() => ModuleRuntime.ValidateLiveTopics(
            [new LiveTopic("post", "content.author"), new LiveTopic("post", "content.accept")]));
        e.Message.ShouldContain("more than once");
    }

    [Theory]
    [InlineData("Post")]
    [InlineData("a:b")]
    [InlineData("")]
    [InlineData("user")]
    public void Invalid_kinds_are_refused_at_install(string kind) =>
        Should.Throw<ModuleException>(() => ModuleRuntime.ValidateLiveTopics([new LiveTopic(kind, "content.view")]));

    [Fact]
    public void Blank_permissions_are_refused_at_install()
    {
        Should.Throw<ModuleException>(() => ModuleRuntime.ValidateLiveTopics([new LiveTopic("post", " ")]));
        Should.Throw<ModuleException>(() => ModuleRuntime.ValidateLiveTopics([new LiveTopic("post", "a") { OrPermissions = [""] }]));
    }

    [Fact]
    public void Valid_topics_pass()
    {
        ModuleRuntime.ValidateLiveTopics([new LiveTopic("post", "content.author"), LiveTopic.AnyOf("unit", "org.read", "org.units.manage"),
            new LiveTopic("draft-2", "content.edit")]);
    }

    [Fact]
    public void The_catalog_refuses_a_kind_an_old_record_declares_twice()
    {
        var topics = ModuleLiveTopicCatalog.OnePerKind("content",
            [new LiveTopic("post", "content.author"), new LiveTopic("post", "content.accept"), new LiveTopic("draft", "content.edit")],
            NullLogger.Instance);

        topics.Select(t => t.Kind).ShouldBe(["draft"]);
    }

    // --- The hub ---

    [Fact]
    public async Task Any_one_of_the_permissions_is_enough()
    {
        await using var alice = await _host.ConnectAsync("alice");
        await using var bob = await _host.ConnectAsync("bob");
        await using var carol = await _host.ConnectAsync("carol");
        await _host.GrantAsync("acme", "alice", "content.accept", "press:A");       // the first permission
        await _host.GrantAsync("acme", "bob", "content.publish", "category:sports"); // the second, on another resolved scope

        (await alice.SubscribeAsync("content:review:42")).Ok.ShouldBeTrue();
        (await bob.SubscribeAsync("content:review:42")).Ok.ShouldBeTrue();
        (await carol.SubscribeAsync("content:review:42")).Error.ShouldBe("forbidden");
    }

    [Fact]
    public async Task Every_permission_still_needs_the_resource()
    {
        await using var alice = await _host.ConnectAsync("alice");
        await _host.GrantAsync("acme", "alice", "content.publish", "press:B");    // the right permission, another press
        await _host.GrantAsync("acme", "alice", "content.view", "press:A");       // the right press, a permission not listed

        (await alice.SubscribeAsync("content:review:42")).Error.ShouldBe("forbidden");
        (await alice.SubscribeAsync("content:review:7")).Error.ShouldBe("forbidden"); // nothing resolved: only review:7 counts
    }

    [Fact]
    public async Task Losing_the_last_permission_revokes_the_subscription()
    {
        await using var bob = await _host.ConnectAsync("bob");
        await _host.GrantAsync("acme", "bob", "content.publish", "press:A");
        (await bob.SubscribeAsync("content:review:42")).Ok.ShouldBeTrue();

        await _host.RevokeAllAsync("acme", "bob");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (await bob.Revoked.Reader.ReadAsync(cts.Token)).ShouldBe("content:review:42");
    }
}
