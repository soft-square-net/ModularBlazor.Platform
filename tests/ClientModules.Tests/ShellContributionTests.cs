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
    public void Titles_follow_the_culture()
    {
        var dock = new ShellContribution("M:chat", ShellContributionKinds.Dock, "Chat", "M.Chat",
            Titles: new Dictionary<string, string> { ["ar"] = "محادثة" });
        dock.TitleIn(new System.Globalization.CultureInfo("ar-EG")).ShouldBe("محادثة");
        dock.TitleIn(new System.Globalization.CultureInfo("en-US")).ShouldBe("Chat");
        dock.ModuleId.ShouldBe("M");
    }
}
