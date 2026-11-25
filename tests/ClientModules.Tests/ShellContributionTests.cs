using soft.square.net.Share.All;

namespace soft.square.net.ClientModules.Tests;

/// <summary>Which context panels apply to a page (Share.All.ShellContribution, used by the client app).</summary>
public sealed class ShellContributionTests
{
    [Theory]
    [InlineData("content/posts/*", "content/posts/12", true)]
    [InlineData("content/posts/*", "content/posts", false)]
    [InlineData("content/posts/*", "content/posts/12/edit", false)]
    [InlineData("content/**", "content", true)]
    [InlineData("content/**", "content/posts/12/edit", true)]
    [InlineData("content/**", "contents/x", false)]
    [InlineData("Content/Posts", "content/posts?tab=2", true)]
    public void Route_patterns(string pattern, string path, bool expected) =>
        ShellContribution.RouteMatches(pattern, path).ShouldBe(expected);

    [Fact]
    public void A_context_panel_shows_for_its_route_or_its_context_key()
    {
        var panel = new ShellContribution("M:comments", ShellContributionKinds.ContextPanel, "Comments", "M.Comments",
            ForRoutes: ["content/posts/*"], ForContext: ["content.post"]);

        panel.AppliesTo("content/posts/7", []).ShouldBeTrue();
        panel.AppliesTo("content", ["content.post"]).ShouldBeTrue();
        panel.AppliesTo("content", ["content.category"]).ShouldBeFalse();
    }

    [Fact]
    public void Other_kinds_always_apply()
    {
        new ShellContribution("M:chat", ShellContributionKinds.Dock, "Chat", "M.Chat").AppliesTo("anything", []).ShouldBeTrue();
    }

    [Fact]
    public void A_dock_panel_is_for_its_studio_and_context()
    {
        var panel = new ShellContribution("Module.Elsa:workflow-status", ShellContributionKinds.DockPanel, "Workflow", "Elsa.Status",
            ForContext: ["content.post"]) { ForStudio = "contentdesigner", Area = DockPanelAreas.Right };

        panel.IsForStudio("contentdesigner").ShouldBeTrue();
        panel.IsForStudio("ContentDesigner", ["content.post"]).ShouldBeTrue();
        panel.IsForStudio("contentdesigner", ["content.category"]).ShouldBeFalse();
        panel.IsForStudio("other").ShouldBeFalse();
        panel.AppliesTo("anything", []).ShouldBeTrue(); // layouts never list dock panels; the studio picks them

        (panel with { ForContext = null }).IsForStudio("contentdesigner", ["content.category"]).ShouldBeTrue();

        new ShellContribution("M:ctx", ShellContributionKinds.ContextPanel, "C", "M.C", ForContext: ["content.post"])
            .IsForStudio("contentdesigner").ShouldBeFalse();
    }

    [Fact]
    public void Dock_panel_fields_and_dialogs_travel_in_the_manifest_json()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var json = System.Text.Json.JsonSerializer.Serialize(
            new soft.square.net.WebApi.Modules.Core.ClientModules.ClientModuleContribution("M:p", "dock-panel", "P", "M.P")
                { ForStudio = "contentdesigner", Area = "bottom", Document = true }, web);
        var read = System.Text.Json.JsonSerializer.Deserialize<ShellContribution>(json, web)!;
        read.ForStudio.ShouldBe("contentdesigner");
        read.Area.ShouldBe("bottom");
        read.Document.ShouldBeTrue();

        var dialog = System.Text.Json.JsonSerializer.Deserialize<ModuleDialogInfo>(System.Text.Json.JsonSerializer.Serialize(
            new soft.square.net.WebApi.Modules.Core.ClientModules.ClientModuleDialog("M/pick", "Pick", ["picker:asset"]), web), web)!;
        dialog.Id.ShouldBe("M/pick");
        dialog.Provides.ShouldBe(["picker:asset"]);
    }

    [Fact]
    public void Dialogs_are_found_by_capability()
    {
        var dialog = new ModuleDialogInfo("Module.Storage/pick-asset", "Pick", ["picker:asset"]);
        dialog.ModuleId.ShouldBe("Module.Storage");
        dialog.HasCapability("picker:asset").ShouldBeTrue();
        dialog.HasCapability("Picker:Asset").ShouldBeTrue();
        dialog.HasCapability("picker:term").ShouldBeFalse();
        new ModuleDialogInfo("M/x").HasCapability("picker:asset").ShouldBeFalse();
    }

    [Fact]
    public void Titles_follow_the_culture()
    {
        var dock = new ShellContribution("M:chat", ShellContributionKinds.Dock, "Chat", "M.Chat",
            Titles: new Dictionary<string, string> { ["ar"] = "محادثة" });
        dock.TitleIn(new System.Globalization.CultureInfo("ar-EG")).ShouldBe("محادثة");
        dock.TitleIn(new System.Globalization.CultureInfo("en-US")).ShouldBe("Chat");
        dock.ModuleId.ShouldBe("M");
    }
}
