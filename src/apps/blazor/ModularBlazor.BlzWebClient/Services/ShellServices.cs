using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using soft.square.net.ModularBlazor.Abstractions.Shell;
using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// What the current page shows (IPageContext). Cleared by App.OnNavigateAsync before the next page renders, so the
/// new page's own Set calls survive (a LocationChanged handler could run after the new page has set its keys).
/// </summary>
public sealed class PageContextService : IPageContext
{
  private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

  public IReadOnlyDictionary<string, string> Values => _values;

  public event Action? Changed;

  public string? Get(string key) => _values.GetValueOrDefault(key);

  public void Set(string key, string value)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    if (_values.TryGetValue(key, out var current) && current == value) return;
    _values[key] = value;
    Changed?.Invoke();
  }

  public void Clear(string key)
  {
    if (_values.Remove(key)) Changed?.Invoke();
  }

  /// <summary>Called on navigation: the next page starts with no context.</summary>
  public void Reset()
  {
    if (_values.Count == 0) return;
    _values.Clear();
    Changed?.Invoke();
  }
}

/// <summary>
/// The header actions, docks and context panels of the catalog's modules (IShellContributions). The catalog is the
/// user's manifest, already filtered by the server, so this only orders the entries and, for context panels, picks
/// those that apply to the current page. Components are resolved from the owning module, loading it when needed.
/// </summary>
public sealed class ShellContributionService : IShellContributions, IDisposable
{
  private readonly ModuleManager _modules;
  private readonly NavigationManager _navigation;
  private readonly PageContextService _page;

  public ShellContributionService(ModuleManager modules, NavigationManager navigation, PageContextService page)
  {
    _modules = modules;
    _navigation = navigation;
    _page = page;
    _modules.Changed += OnChanged;
    _page.Changed += OnChanged;
    _navigation.LocationChanged += OnLocationChanged;
  }

  public event Action? Changed;

  public IReadOnlyList<ShellContribution> Of(string kind)
  {
    var path = ModuleManager.NormalizePath(_navigation.ToBaseRelativePath(_navigation.Uri));
    return _modules.Catalog
      .SelectMany(m => m.Contributions)
      .Where(c => c.Kind == kind && c.AppliesTo(path, _page.Values.Keys))
      .OrderBy(c => c.Order)
      .ThenBy(c => c.ModuleId, StringComparer.OrdinalIgnoreCase)
      .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public async Task<Type?> ResolveAsync(ShellContribution contribution, CancellationToken cancellationToken = default)
  {
    var moduleId = contribution.ModuleId;
    if (_modules.GetDescriptor(moduleId) is null) return null; // no longer in the user's catalog
    try
    {
      await _modules.EnsureLoadedAsync(moduleId, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      Console.Error.WriteLine($"[shell] {contribution.Id}: module '{moduleId}' failed to load: {ex.Message}");
      return null;
    }

    var type = _modules.FindModuleType(moduleId, contribution.Component);
    if (type is null || !typeof(IComponent).IsAssignableFrom(type))
    {
      Console.Error.WriteLine($"[shell] {contribution.Id}: '{contribution.Component}' is not a component of module '{moduleId}'.");
      return null;
    }
    return type;
  }

  private void OnChanged() => Changed?.Invoke();

  private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => Changed?.Invoke();

  public void Dispose()
  {
    _modules.Changed -= OnChanged;
    _page.Changed -= OnChanged;
    _navigation.LocationChanged -= OnLocationChanged;
  }
}
