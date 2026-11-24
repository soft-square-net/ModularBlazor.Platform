using Microsoft.AspNetCore.Components;
using soft.square.net.ModularBlazor.Abstractions;
using soft.square.net.ModularBlazor.Abstractions.Shell;

[assembly: ClientModule(Id = "Module.Fixture", Name = "Fixture", RoutePrefixes = new[] { "fixture" }, Styles = new[] { "css/fixture.css" })]

[assembly: MenuLink("posts", "Posts", "fixture/posts", Permissions = new[] { "fixture.posts.read", "bad name!" })]
[assembly: MenuLink("all-bad", "Hidden", "fixture/hidden", Permissions = new[] { "no spaces allowed" })]
[assembly: MenuLink("open", "Open", "fixture")]

[assembly: HeaderAction("bell", "Notifications", typeof(soft.square.net.ClientModules.Fixture.Bell), Icon = "bell", Order = 10)]
[assembly: Dock("chat", "Chat", typeof(soft.square.net.ClientModules.Fixture.Panels.Chat), Badge = "api/fixture/unread", BadgeRefresh = 5,
    Permissions = new[] { "fixture.chat" })]
[assembly: ContextPanel("comments", "Comments", typeof(soft.square.net.ClientModules.Fixture.Panels.Comments),
    ForRoutes = new[] { "/fixture/posts/*/", "fixture/**/x", "bad route!" }, ForContext = new[] { "fixture.post", "NotAKey" })]
[assembly: ContextPanel("nowhere", "Nowhere", typeof(soft.square.net.ClientModules.Fixture.Bell))]
[assembly: Dock("missing", "Missing", typeof(string))]

namespace soft.square.net.ClientModules.Fixture
{
  public sealed class Bell : ComponentBase;

  [LayoutKey("fixture")]
  [LayoutInfo(LayoutRoles.App, Sections = new[] { LayoutSections.Title }, Zones = new[] { "main" }, Contributions = new[] { "dock" })]
  public sealed class FixtureLayout : LayoutComponentBase;

  [Route("/fixture")]
  public sealed class Home : ComponentBase;
}

namespace soft.square.net.ClientModules.Fixture.Panels
{
  public sealed class Chat : ComponentBase;

  public static class Outer
  {
    public sealed class Nested : ComponentBase;
  }

  public sealed class Comments : ComponentBase;
}
