using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using soft.square.net.Layouts.Default.Shell;
using soft.square.net.ModularBlazor.Abstractions.Live;

namespace soft.square.net.ModularBlazor.Client.Tests;

/// <summary>The Default layout's connection badge follows the channel's state.</summary>
public sealed class LiveBadgeTests : BunitContext
{
    public LiveBadgeTests() => Services.AddSingleton(typeof(IStringLocalizer<>), typeof(KeyLocalizer<>));

    [Fact]
    public void It_shows_the_state_and_follows_its_changes()
    {
        var channel = new FakeChannel();
        Services.AddSingleton<ILiveChannel>(channel);

        var badge = Render<LiveBadge>();
        badge.Markup.Trim().ShouldBeEmpty(); // offline: signed out

        channel.Set(LiveState.Reconnecting);
        badge.WaitForAssertion(() => badge.Find(".shell-live").GetAttribute("data-live").ShouldBe("reconnecting"));
        badge.Find(".shell-live").ClassList.ShouldContain("shell-badge--warning");

        channel.Set(LiveState.Connected);
        badge.WaitForAssertion(() => badge.Find(".shell-live").ClassList.ShouldContain("shell-badge--success"));
        badge.Find(".shell-live").TextContent.Trim().ShouldBe("Live");
    }

    [Fact]
    public void Without_a_live_channel_it_renders_nothing()
    {
        Render<LiveBadge>().Markup.Trim().ShouldBeEmpty();
    }

    private sealed class FakeChannel : ILiveChannel
    {
        public LiveState State { get; private set; }
        public event Action<LiveState>? StateChanged;
        public event Func<LiveResync, Task>? Resync { add { } remove { } }

        public void Set(LiveState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        public Task<IAsyncDisposable> SubscribeAsync(string topic, Func<LiveMessage, Task> handler, long? fromSequence = null,
            LiveSubscribeOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class KeyLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
