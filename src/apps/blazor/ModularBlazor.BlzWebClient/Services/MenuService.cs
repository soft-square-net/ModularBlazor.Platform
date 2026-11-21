using soft.square.net.ModularBlazor.Abstractions.Menus;
using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// The menus (IMenuService), built from the module catalog: every module the user may see brings its menu groups and
/// links (MenuGroup / MenuLink, read from its metadata on upload), so they are there before its code is downloaded and
/// go with it when it leaves the catalog (deactivated, deleted, no longer for this user). Rebuilt whenever the catalog
/// changes.
/// <list type="bullet">
/// <item>A module's old single entry (ClientModule NavTitle/NavHref) is a link in the main menu, unless the module
/// declares a menu of its own.</item>
/// <item>The app's own admin pages (module management) go in the secondary menu.</item>
/// <item>A child is shown in its parent's zone; a parent that is not there (its module is not in the catalog) puts the
/// child at the top of its own zone. A group with nothing in it and no link of its own is left out.</item>
/// <item>Order: Order, then the title in the current language.</item>
/// </list>
/// </summary>
public sealed class MenuService : IMenuService, IDisposable
{
  private readonly ModuleManager _modules;
  private Dictionary<string, IReadOnlyList<MenuNode>>? _zones; // built on first use after a change
  private string? _culture;                                    // ...in this UI culture (the titles' order depends on it)

  public MenuService(ModuleManager modules)
  {
    _modules = modules;
    _modules.Changed += OnCatalogChanged;
  }

  public event Action? Changed;

  public IReadOnlyList<MenuNode> Zone(string zone)
  {
    var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
    if (_zones is null || _culture != culture) (_zones, _culture) = (Build(Items()), culture);
    return _zones.GetValueOrDefault(zone.ToLowerInvariant()) ?? [];
  }

  private void OnCatalogChanged()
  {
    _zones = null;
    Changed?.Invoke();
  }

  public void Dispose() => _modules.Changed -= OnCatalogChanged;

  /// <summary>Every entry the catalog brings, ids unique (the first one wins).</summary>
  private List<MenuItem> Items()
  {
    var items = new List<MenuItem>();
    foreach (var module in _modules.Catalog.Where(d => !d.Hidden))
    {
      if (module.Menu.Length > 0) items.AddRange(module.Menu);
      else
        items.AddRange(module.Nav.Select((n, i) =>
            new MenuItem($"{module.Id}:nav-{i}", MenuItemKinds.Link, MenuZones.Main, n.Title, n.Href, Order: n.Order, Titles: n.Titles)));
    }
    items.AddRange(_modules.HostNav.Select(n =>
        new MenuItem($"host:{n.Href}", MenuItemKinds.Link, MenuZones.Secondary, n.Title, n.Href, Order: n.Order, Titles: n.Titles)));
    return items.DistinctBy(i => i.Id, StringComparer.OrdinalIgnoreCase).ToList();
  }

  /// <summary>The zones' trees. Pure: public for tests.</summary>
  public static Dictionary<string, IReadOnlyList<MenuNode>> Build(IReadOnlyList<MenuItem> items)
  {
    var byId = items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

    MenuItem? GroupOver(MenuItem item) =>
        item.Parent is { } p && byId.TryGetValue(p, out var parent) && parent.IsGroup ? parent : null;

    // The group each entry is shown in: its parent when that is a group in the catalog, unless the entry is in a cycle
    // (A in B, B in A): then none, the top of its own zone. What is under a group of a cycle stays under it.
    string? ParentOf(MenuItem item)
    {
      if (GroupOver(item) is not { } parent) return null;
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      for (var current = parent; current is not null && seen.Add(current.Id); current = GroupOver(current))
        if (current.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)) return null;
      return parent.Id;
    }

    var parents = items.ToDictionary(i => i.Id, ParentOf, StringComparer.OrdinalIgnoreCase);
    var children = items.Where(i => parents[i.Id] is not null)
        .GroupBy(i => parents[i.Id]!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    MenuNode? Node(MenuItem item)
    {
      var kids = Sorted(children.GetValueOrDefault(item.Id) ?? []).Select(Node).OfType<MenuNode>().ToList();
      // A group with nothing in it (its modules are not in the catalog) and no page of its own: left out.
      return item.IsGroup && kids.Count == 0 && string.IsNullOrEmpty(item.Href) ? null : new MenuNode(item, kids);
    }

    return Sorted(items.Where(i => parents[i.Id] is null))
        .Select(Node).OfType<MenuNode>()
        .GroupBy(n => n.Item.Zone.ToLowerInvariant())
        .ToDictionary(g => g.Key, g => (IReadOnlyList<MenuNode>)g.ToList(), StringComparer.OrdinalIgnoreCase);
  }

  private static IEnumerable<MenuItem> Sorted(IEnumerable<MenuItem> items) =>
      items.OrderBy(i => i.Order).ThenBy(i => i.LocalTitle, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Id, StringComparer.Ordinal);
}
