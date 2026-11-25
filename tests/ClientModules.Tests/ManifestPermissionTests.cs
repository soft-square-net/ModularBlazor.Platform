using soft.square.net.WebApi.Modules.Core.ClientModules;

namespace soft.square.net.ClientModules.Tests;

/// <summary>The manifest hides menu entries and shell contributions the caller has none of the permissions for.</summary>
public sealed class ManifestPermissionTests
{
    private static ClientModuleDescriptor Module(string id, ClientModuleMenuItem[] menu, ClientModuleContribution[]? contributions = null) =>
        new(id, id, "1.0.0", "dll", $"api/client-modules/files/{id}", "00", [], [], ClientModuleLoadStrategy.Lazy, false, false, false,
            null, [], [], null, [], [], [], [], menu, contributions ?? []);

    private static ClientModuleMenuItem Link(string id, string? parent = null, params string[] permissions) =>
        new(id, "link", "main", id, Href: id, Parent: parent, Permissions: permissions);

    private static ClientModuleMenuItem Group(string id, string? parent = null, params string[] permissions) =>
        new(id, "group", "main", id, Parent: parent, Permissions: permissions);

    private static ClientModuleContribution Dock(string id, params string[] permissions) =>
        new(id, "dock", id, "Fixture.Dock", Permissions: permissions);

    private static IReadOnlyList<string> MenuIds(IEnumerable<ClientModuleDescriptor> modules) =>
        modules.SelectMany(m => m.Menu).Select(i => i.Id).ToList();

    [Fact]
    public void Entries_without_permissions_are_for_everyone()
    {
        var modules = new List<ClientModuleDescriptor> { Module("posts", [Link("posts.all"), Group("posts")]) };

        var filtered = ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string>());

        filtered[0].ShouldBeSameAs(modules[0]);
    }

    [Fact]
    public void An_entry_needs_one_of_its_permissions()
    {
        var modules = new List<ClientModuleDescriptor>
        {
            Module("posts", [Link("posts.all"), Link("posts.new", null, "posts.write", "posts.admin"), Link("posts.trash", null, "posts.admin")])
        };

        MenuIds(ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string> { "posts.write" }))
            .ShouldBe(["posts.all", "posts.new"]);
        MenuIds(ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string>()))
            .ShouldBe(["posts.all"]);
    }

    [Fact]
    public void A_hidden_group_hides_its_children_in_every_module_and_their_children()
    {
        var modules = new List<ClientModuleDescriptor>
        {
            Module("admin", [Group("admin", null, "platform.modules.manage"), Group("admin.content", "admin"), Link("admin.content.tags", "admin.content")]),
            Module("tenants", [Link("tenants.list", "admin"), Link("tenants.public")])
        };

        var filtered = ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string>());

        MenuIds(filtered).ShouldBe(["tenants.public"]);
        MenuIds(ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string> { "platform.modules.manage" }))
            .Count.ShouldBe(5);
    }

    [Fact]
    public void Contributions_need_one_of_their_permissions()
    {
        var modules = new List<ClientModuleDescriptor>
        {
            Module("chat", [], [Dock("chat.dock"), Dock("chat.moderation", "chat.moderate")])
        };

        ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string>())[0].Contributions.Select(c => c.Id)
            .ShouldBe(["chat.dock"]);
        ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string> { "chat.moderate" })[0].Contributions.Count
            .ShouldBe(2);
    }

    [Fact]
    public void Studio_dock_panels_need_one_of_their_permissions_and_keep_their_studio()
    {
        var status = new ClientModuleContribution("elsa:status", "dock-panel", "Workflow", "Elsa.Status", Permissions: ["elsa.read"])
            { ForStudio = "contentdesigner", Area = "right" };
        var modules = new List<ClientModuleDescriptor>
        {
            Module("elsa", [], [status, Dock("elsa.dock")]) with { Dialogs = [new("elsa/pick", null, ["picker:workflow"])] }
        };

        var anonymous = ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string>())[0];
        anonymous.Contributions.Select(c => c.Id).ShouldBe(["elsa.dock"]);
        anonymous.Dialogs.ShouldHaveSingleItem().Id.ShouldBe("elsa/pick"); // `with` keeps the dialog list

        var granted = ClientModuleCatalog.FilterByPermissions(modules, new HashSet<string> { "elsa.read" })[0];
        granted.Contributions.Single(c => c.Id == "elsa:status").ForStudio.ShouldBe("contentdesigner");
    }
}
