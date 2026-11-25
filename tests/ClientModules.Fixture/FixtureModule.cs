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

// Studio dock panels (DockPanelAttribute) for a studio keyed "fixture-studio".
[assembly: DockPanel("status", "Status", typeof(soft.square.net.ClientModules.Fixture.Panels.Status), ForStudio = "fixture-studio",
    ForContext = new[] { "fixture.post", "Not A Key" }, Area = "Bottom", Permissions = new[] { "fixture.status" }, Order = 5)]
[assembly: DockPanel("preview", "Preview", typeof(soft.square.net.ClientModules.Fixture.Panels.Outer.Nested), ForStudio = "fixture-studio",
    Document = true, Area = "left")]
[assembly: DockPanel("tools", "Tools", typeof(soft.square.net.ClientModules.Fixture.Bell), ForStudio = "fixture-studio", Area = "top")]
[assembly: DockPanel("nostudio", "No studio", typeof(soft.square.net.ClientModules.Fixture.Bell))]

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

  public sealed class Status : ComponentBase;
}

namespace soft.square.net.ClientModules.Fixture.Dialogs
{
  // A picker found by capability; the bad capability is dropped with a warning.
  [ClientDialog("Module.Fixture/pick-file", Title = "Pick a file", Provides = new[] { "picker:asset", "Not A Capability" })]
  public sealed class PickFile : ComponentBase;

  // No capabilities: listed, found by id only.
  [ClientDialog("Module.Fixture/confirm")]
  public sealed class Confirm : ComponentBase;

  // Another module's id: left out.
  [ClientDialog("Module.Other/steal", Provides = new[] { "picker:asset" })]
  public sealed class Steal : ComponentBase;
}
