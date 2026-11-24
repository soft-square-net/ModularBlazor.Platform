namespace soft.square.net.ModularBlazor.Abstractions.Shell;

/// <summary>
/// The jobs a layout does. A page or module asks for a role (or a layout key); a tenant's theme setting picks which
/// layout module fills each role, so swapping the look does not touch the modules.
/// </summary>
public static class LayoutRoles
{
  /// <summary>The signed-in application: menu drawer, top bar, docks.</summary>
  public const string App = "app";

  /// <summary>Platform and tenant administration.</summary>
  public const string Admin = "admin";

  /// <summary>The public site (news, sport, culture): masthead, sections, footer.</summary>
  public const string Public = "public";

  /// <summary>Sign-in and account pages: a centred card.</summary>
  public const string Auth = "auth";

  /// <summary>No chrome at all (print views, embeds).</summary>
  public const string Blank = "blank";

  public static readonly IReadOnlyList<string> All = [App, Admin, Public, Auth, Blank];
}

/// <summary>
/// The parts of a layout a page fills, through Blazor's sections (<c>&lt;SectionContent SectionName="@LayoutSections.Title"&gt;</c>,
/// or <see cref="PageHeader"/>). The layout places a <c>SectionOutlet</c> for each one it shows; one it does not show
/// is simply not rendered. Section content belongs to the page, so it goes when the page does.
/// </summary>
public static class LayoutSections
{
  /// <summary>The page's heading in the top bar.</summary>
  public const string Title = "mb-title";

  /// <summary>The trail above or beside the title.</summary>
  public const string Breadcrumbs = "mb-breadcrumbs";

  /// <summary>The page's own buttons (Save, New, …), at the end of the top bar or the page header.</summary>
  public const string PageActions = "mb-page-actions";

  /// <summary>The page's own side panel, next to the module-contributed context panels.</summary>
  public const string ContextPanel = "mb-context-panel";

  public static readonly IReadOnlyList<string> All = [Title, Breadcrumbs, PageActions, ContextPanel];
}

/// <summary>
/// What the layout hands a contributed component (<c>[Parameter] public ContributionContext Context { get; set; }</c>,
/// optional): which entry it is, and for a context panel the page-context key and value it was opened for.
/// </summary>
/// <param name="ContributionId">"{moduleId}:{localId}".</param>
/// <param name="ContextKey">The page-context key that made a context panel show (null: it showed for the route).</param>
/// <param name="ContextValue">That key's value, e.g. the id of the post the page has selected.</param>
public sealed record ContributionContext(string ContributionId, string? ContextKey = null, string? ContextValue = null);
