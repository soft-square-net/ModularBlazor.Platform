using System.Reflection;
using soft.square.net.WebApi.Modules.Core.ClientModules;

namespace soft.square.net.ClientModules.Tests;

/// <summary>The inspector reads the layout contract from a compiled module (ClientModules.Fixture) without loading it.</summary>
public sealed class InspectorShellTests
{
    private static readonly Lazy<InspectedPackage> Package = new(() =>
    {
        var path = typeof(InspectorShellTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "FixtureModule").Value!;
        return ClientModuleInspector.Inspect(Path.GetFileName(path), File.ReadAllBytes(path));
    });

    private static InspectedPackage Fixture => Package.Value;

    [Fact]
    public void Reads_header_actions_docks_and_context_panels()
    {
        Fixture.Contributions.Where(c => c.Kind != "dock-panel").Select(c => c.Id)
            .ShouldBe(["Module.Fixture:bell", "Module.Fixture:chat", "Module.Fixture:comments"], ignoreOrder: true);

        var bell = Fixture.Contributions.Single(c => c.Id == "Module.Fixture:bell");
        bell.Kind.ShouldBe("header-action");
        bell.Component.ShouldBe("soft.square.net.ClientModules.Fixture.Bell");
        bell.Icon.ShouldBe("bell");
        bell.Order.ShouldBe(10);
        bell.Permissions.ShouldBeNull();

        var chat = Fixture.Contributions.Single(c => c.Id == "Module.Fixture:chat");
        chat.Kind.ShouldBe("dock");
        chat.Component.ShouldBe("soft.square.net.ClientModules.Fixture.Panels.Chat");
        chat.Badge.ShouldBe("api/fixture/unread");
        chat.BadgeRefresh.ShouldBe(15); // clamped
        chat.Permissions.ShouldBe(["fixture.chat"]);
    }

    [Fact]
    public void Reads_studio_dock_panels()
    {
        Fixture.Contributions.Where(c => c.Kind == "dock-panel").Select(c => c.Id)
            .ShouldBe(["Module.Fixture:status", "Module.Fixture:preview", "Module.Fixture:tools"], ignoreOrder: true);

        var status = Fixture.Contributions.Single(c => c.Id == "Module.Fixture:status");
        status.ForStudio.ShouldBe("fixture-studio");
        status.Component.ShouldBe("soft.square.net.ClientModules.Fixture.Panels.Status");
        status.ForContext.ShouldBe(["fixture.post"]);
        status.Area.ShouldBe("bottom"); // lower-cased
        status.Document.ShouldBeFalse();
        status.Permissions.ShouldBe(["fixture.status"]);
        status.Order.ShouldBe(5);
        status.Badge.ShouldBeNull();
        Fixture.Warnings.ShouldContain(w => w.StartsWith("DockPanel 'status'") && w.Contains("'Not A Key'"));

        var preview = Fixture.Contributions.Single(c => c.Id == "Module.Fixture:preview");
        preview.Component.ShouldBe("soft.square.net.ClientModules.Fixture.Panels.Outer+Nested");
        preview.Document.ShouldBeTrue();
        preview.Area.ShouldBeNull(); // a document opens in the centre
        preview.ForContext.ShouldBeNull();
        Fixture.Warnings.ShouldContain(w => w.StartsWith("DockPanel 'preview'") && w.Contains("centre"));

        var tools = Fixture.Contributions.Single(c => c.Id == "Module.Fixture:tools");
        tools.Area.ShouldBe("right");
        Fixture.Warnings.ShouldContain(w => w.StartsWith("DockPanel 'tools'") && w.Contains("'top'"));
    }

    [Fact]
    public void A_dock_panel_without_a_studio_is_left_out()
    {
        Fixture.Contributions.ShouldNotContain(c => c.Id == "Module.Fixture:nostudio");
        Fixture.Warnings.ShouldContain(w => w.StartsWith("DockPanel 'nostudio'") && w.Contains("ForStudio"));
    }

    [Fact]
    public void Reads_dialogs_and_their_capabilities()
    {
        Fixture.Dialogs.Select(d => d.Id).ShouldBe(["Module.Fixture/pick-file", "Module.Fixture/confirm"], ignoreOrder: true);

        var pick = Fixture.Dialogs.Single(d => d.Id == "Module.Fixture/pick-file");
        pick.Title.ShouldBe("Pick a file");
        pick.Provides.ShouldBe(["picker:asset"]);
        Fixture.Warnings.ShouldContain(w => w.Contains("'Not A Capability'"));

        Fixture.Dialogs.Single(d => d.Id == "Module.Fixture/confirm").Provides.ShouldBeNull();
        Fixture.Warnings.ShouldContain(w => w.StartsWith("ClientDialog 'Module.Other/steal'"));
    }

    [Fact]
    public void Context_panels_keep_valid_routes_and_keys_only()
    {
        var comments = Fixture.Contributions.Single(c => c.Id == "Module.Fixture:comments");
        comments.Kind.ShouldBe("context-panel");
        comments.ForRoutes.ShouldBe(["fixture/posts/*"]);
        comments.ForContext.ShouldBe(["fixture.post"]);
        Fixture.Warnings.ShouldContain(w => w.Contains("'fixture/**/x'"));
        Fixture.Warnings.ShouldContain(w => w.Contains("'bad route!'"));
        Fixture.Warnings.ShouldContain(w => w.Contains("'NotAKey'"));
    }

    [Fact]
    public void Leaves_out_entries_that_cannot_work()
    {
        Fixture.Contributions.ShouldNotContain(c => c.Id == "Module.Fixture:nowhere");
        Fixture.Warnings.ShouldContain(w => w.StartsWith("ContextPanel 'nowhere'"));
        Fixture.Contributions.ShouldNotContain(c => c.Id == "Module.Fixture:missing");
        Fixture.Warnings.ShouldContain(w => w.StartsWith("Dock 'missing'") && w.Contains("System.String"));
    }

    [Fact]
    public void Menu_entries_carry_their_permissions()
    {
        Fixture.Menu.Single(m => m.Id == "Module.Fixture:posts").Permissions.ShouldBe(["fixture.posts.read"]);
        Fixture.Warnings.ShouldContain(w => w.Contains("'bad name!'"));

        // Every name invalid: hidden from everyone rather than shown to everyone.
        Fixture.Menu.Single(m => m.Id == "Module.Fixture:all-bad").Permissions.ShouldBe([ClientModuleInspector.InvalidPermission]);
        Fixture.Menu.Single(m => m.Id == "Module.Fixture:open").Permissions.ShouldBeNull();
    }

    [Fact]
    public void Reads_layout_details_and_platform_version()
    {
        var layout = Fixture.LayoutDetails.ShouldHaveSingleItem();
        layout.Key.ShouldBe("fixture");
        layout.Role.ShouldBe("app");
        layout.Sections.ShouldBe(["mb-title"]);
        layout.Zones.ShouldBe(["main"]);
        layout.Contributions.ShouldBe(["dock"]);

        Fixture.PlatformVersion.ShouldNotBeNull();
        Fixture.Warnings.ShouldNotContain(w => w.Contains("built for platform"));
    }

    [Fact]
    public void Platform_version_rules()
    {
        var saved = ClientModuleInspector.HostPlatformVersion;
        try
        {
            ClientModuleInspector.HostPlatformVersion = new Version(2, 3, 0);

            var warnings = new List<string>();
            ClientModuleInspector.CheckPlatformVersion("m.dll", new Version(2, 3, 9), warnings);
            ClientModuleInspector.CheckPlatformVersion("m.dll", null, warnings);
            warnings.ShouldBeEmpty();

            ClientModuleInspector.CheckPlatformVersion("m.dll", new Version(2, 4, 0), warnings);
            warnings.ShouldHaveSingleItem().ShouldContain("features it uses may be missing");

            warnings.Clear();
            ClientModuleInspector.CheckPlatformVersion("m.dll", new Version(1, 9, 0), warnings);
            warnings.ShouldHaveSingleItem().ShouldContain("older major");

            Should.Throw<ClientModuleException>(() => ClientModuleInspector.CheckPlatformVersion("m.dll", new Version(3, 0, 0), []))
                .Message.ShouldContain("Update the platform first");
        }
        finally
        {
            ClientModuleInspector.HostPlatformVersion = saved;
        }
    }
}
