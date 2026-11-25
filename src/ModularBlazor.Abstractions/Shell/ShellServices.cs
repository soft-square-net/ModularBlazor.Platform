using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.Abstractions.Shell;

/// <summary>
/// The header actions, docks and context panels of the modules the user may see, built by the host from their
/// metadata. Layouts show them; modules declare them with the attributes, never through this service.
/// </summary>
public interface IShellContributions
{
  /// <summary>
  /// The entries of a kind (<see cref="ShellContributionKinds"/>) in order: <c>Order</c>, then module id, then id.
  /// Context panels: only those that apply to the current page (route or page context).
  /// </summary>
  IReadOnlyList<ShellContribution> Of(string kind);

  /// <summary>
  /// The entry's component type, loading its module first when it is not loaded yet. Null when the module cannot be
  /// loaded or no longer has the component (the layout then shows the entry as unavailable).
  /// </summary>
  Task<Type?> ResolveAsync(ShellContribution contribution, CancellationToken cancellationToken = default);

  /// <summary>Raised when the entries change: sign-in, sign-out, a module loaded or unloaded, navigation, page context.</summary>
  event Action? Changed;
}

/// <summary>
/// The dock panels other modules contribute to a studio (<see cref="DockPanelAttribute"/>), for the studio that hosts
/// them. The list comes from the user's catalog (the server has left out the panels the user may not see), so it is
/// known before the contributing modules are downloaded. The studio mounts a panel with MudDock, using the host's
/// <see cref="StudioPanels.RootComponent"/> as the component and the panel's id as the dock definition key.
/// </summary>
public interface IStudioPanels
{
  /// <summary>
  /// The panels for the studio <paramref name="studio"/> in order (<c>Order</c>, module id, id). With
  /// <paramref name="contextKeys"/>: only those whose <c>ForContext</c> is empty or holds one of the keys.
  /// </summary>
  IReadOnlyList<ShellContribution> For(string studio, IEnumerable<string>? contextKeys = null);

  /// <summary>One panel by its id ("{moduleId}:{localId}"), or null when the user's catalog has no such dock panel.</summary>
  ShellContribution? Find(string id);

  /// <summary>Raised when the list may have changed: sign-in, sign-out, a module loaded, unloaded or replaced.</summary>
  event Action? Changed;
}

/// <summary>Names the host gives studios for mounting contributed panels.</summary>
public static class StudioPanels
{
  /// <summary>
  /// The host's generic JS root component (<c>Blazor.rootComponents.add(element, "mb-module-panel", { panelKey })</c>).
  /// It renders the dock panel whose id is its <c>PanelKey</c> (or <c>Contribution</c>) parameter, loading its module
  /// first, inside an error boundary and the cascades a page gets (sign-in state, culture, direction).
  /// </summary>
  public const string RootComponent = "mb-module-panel";
}

/// <summary>
/// What the current page is showing, for the context panels other modules add (<see cref="ContextPanelAttribute.ForContext"/>).
/// A page sets it when it opens, and a list page when a row is selected:
/// <c>PageContext.Set(ContentContext.Post, post.Id.ToString())</c>. Everything is cleared on navigation.
/// </summary>
public interface IPageContext
{
  /// <summary>The keys and values set by the current page.</summary>
  IReadOnlyDictionary<string, string> Values { get; }

  /// <summary>The value of a key, or null.</summary>
  string? Get(string key);

  /// <summary>Sets (or replaces) a key's value. Keys are "{module}.{entity}", declared as constants by the owning module.</summary>
  void Set(string key, string value);

  /// <summary>Removes a key (nothing selected any more).</summary>
  void Clear(string key);

  event Action? Changed;
}

/// <summary>The current tenant's brand, for layouts (name, logo) and the theme (colours, mode).</summary>
public interface IBranding
{
  BrandInfo Current { get; }
  event Action? Changed;
}

/// <summary>
/// The current user's permissions in the current tenant, for hiding what the user cannot use. It shapes the UI only:
/// the API checks every call itself.
/// </summary>
public interface IUserPermissions
{
  /// <summary>False until the permissions have been fetched (and while signed out): treat everything as not granted.</summary>
  bool IsLoaded { get; }

  /// <summary>Whether the user holds the permission in any scope of the current tenant.</summary>
  bool Has(string permission);

  /// <summary>Whether the user holds any of them; an empty list is always true.</summary>
  bool HasAny(IEnumerable<string> permissions);

  event Action? Changed;
}
