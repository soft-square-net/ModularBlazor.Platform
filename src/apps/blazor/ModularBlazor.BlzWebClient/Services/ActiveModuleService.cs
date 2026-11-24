using Microsoft.JSInterop;
using soft.square.net.ModularBlazor.BlzWebClient.Auth;
using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// Owns the *browser-side lifecycle* of modules: which modules' styles and scripts are live in the page right now.
///
/// Loading a module's assembly happens once (ModuleManager) and cannot be undone cheaply. Activation is cheap and
/// repeatable, so it follows navigation:
///
///   desired = every loaded LoadOnStartup module (layouts, login: always on)
///           + the module of the current route, with its dependencies (e.g. its layout module)
///
/// <see cref="SyncAsync"/> diffs desired against what is applied and only activates/deactivates the difference
/// (a reconciler, like a UI diff). Calls are serialized, so rapid navigation cannot interleave and the last request wins.
///
/// Where a module's files come from: the server's URLs when the manifest gives an AssetBase (real files: chunks,
/// relative url(...) and imports work; the browser checks each file's hash), otherwise the copies inside the module's
/// package, handed over from memory as blobs (older servers, and the cached manifest while offline).
/// </summary>
public sealed class ActiveModuleService
{
  private const string EntryScript = "js/module.js"; // used when a module declares no Scripts

  private readonly ModuleManager _modules;
  private readonly ModuleAssetStore _store;
  private readonly IJSRuntime _js;
  private readonly Uri? _webApi;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);

  public ActiveModuleService(ModuleManager modules, ModuleAssetStore store, IJSRuntime js, IHttpClientFactory httpClients)
  {
    _modules = modules;
    _store = store;
    _js = js;
    _webApi = httpClients.CreateClient(AuthServiceExtensions.WebApiPublicClient).BaseAddress;

    // A module that was unloaded, or a user's modules forgotten at logout, must also leave the page.
    _modules.Changed += () => _ = SyncAsync();
  }

  public event Action? Changed;

  /// <summary>The module that owns the current route (null on public pages).</summary>
  public string? CurrentModuleId { get; private set; }

  public IReadOnlyCollection<string> ActiveModuleIds => _active.ToArray();

  // Modules kept active besides the page's own: those whose dialogs are open (counted: one dialog may open another).
  private readonly Dictionary<string, int> _held = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>Keeps a module's styles and scripts on the page until <see cref="ReleaseAsync"/> (e.g. while its dialog is open).</summary>
  public Task HoldAsync(string id)
  {
    _held[id] = _held.GetValueOrDefault(id) + 1;
    return SyncAsync();
  }

  public Task ReleaseAsync(string id)
  {
    if (!_held.TryGetValue(id, out var count)) return Task.CompletedTask;
    if (count <= 1) _held.Remove(id);
    else _held[id] = count - 1;
    return SyncAsync();
  }

  /// <summary>The module of the layout the current page is shown in (its styles stay while that layout does).</summary>
  public string? LayoutModuleId { get; private set; }

  /// <summary>Called by the route view once it knows the page's layout: keeps that layout's module active.</summary>
  public Task UseLayoutAsync(string? moduleId)
  {
    if (string.Equals(LayoutModuleId, moduleId, StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
    LayoutModuleId = moduleId;
    return SyncAsync();
  }

  /// <summary>Called by the router before it resolves a URL: make the page's modules the active ones.</summary>
  public Task NavigateAsync(ModuleDescriptor? module)
  {
    CurrentModuleId = module?.Id;
    return SyncAsync();
  }

  public async Task SyncAsync()
  {
    await _gate.WaitAsync();
    try
    {
      var desired = ComputeDesired();

      foreach (var id in _active.Where(a => !desired.Contains(a)).ToList())
        await DeactivateAsync(id);

      foreach (var id in desired.Where(d => !_active.Contains(d)))
        await ActivateAsync(id);
    }
    finally
    {
      _gate.Release();
    }

    Changed?.Invoke();
  }

  // Dependencies first, so a layout's scripts run before the page module's.
  private List<string> ComputeDesired()
  {
    var ordered = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void Add(string id)
    {
      foreach (var m in _modules.GetDependencyClosure(id))
        if (_modules.IsLoaded(m) && seen.Add(m)) ordered.Add(m);
    }

    foreach (var d in _modules.Catalog.Where(d => d.LoadOnStartup)) Add(d.Id);
    if (LayoutModuleId is { } layout) Add(layout);
    if (CurrentModuleId is { } current) Add(current);
    foreach (var held in _held.Keys) Add(held);
    return ordered;
  }

  private async Task ActivateAsync(string id)
  {
    var descriptor = _modules.GetDescriptor(id);
    if (descriptor is null) return;

    var assets = _store.Get(id);
    var styles = new List<StyleSpec>();
    var scripts = new List<ScriptSpec>();

    for (var i = 0; i < descriptor.Styles.Length; i++)
    {
      var path = descriptor.Styles[i];
      var specId = $"{id}#css{i}";
      if (Served(descriptor, path) is { } served) styles.Add(new StyleSpec(specId, null, served.Url, served.Integrity));
      else if (assets.TryGetText(path, out var css)) styles.Add(new StyleSpec(specId, css, null, null));
      else styles.Add(new StyleSpec(specId, null, path, null)); // not packaged: a normal url
    }

    var hasEntry = assets.Contains(EntryScript) || Served(descriptor, EntryScript) is not null;
    var declared = descriptor.Scripts.Length > 0 ? descriptor.Scripts : (hasEntry ? [EntryScript] : []);
    for (var i = 0; i < declared.Length; i++)
    {
      var path = declared[i];
      var specId = $"{id}#js{i}";
      if (Served(descriptor, path) is { } served) scripts.Add(new ScriptSpec(specId, null, served.Url, served.Integrity));
      else if (assets.TryGetText(path, out var code)) scripts.Add(new ScriptSpec(specId, code, null, null, ModuleAssetSet.Normalize(path), OtherScripts(assets, path)));
      else scripts.Add(new ScriptSpec(specId, null, path, null));
    }

    try
    {
      if (styles.Count > 0 || scripts.Count > 0)
      {
        var errors = await _js.InvokeAsync<string[]>("moduleHost.activate", id, new { styles, scripts });
        foreach (var e in errors) Console.Error.WriteLine($"[module {id}] {e}");
      }
    }
    catch (JSException ex)
    {
      // A broken module script must never take navigation down with it.
      Console.Error.WriteLine($"[module {id}] activation failed: {ex.Message}");
    }

    _active.Add(id); // even after a failure, so a broken script is not retried on every navigation
  }

  private async Task DeactivateAsync(string id)
  {
    try
    {
      await _js.InvokeVoidAsync("moduleHost.deactivate", id);
    }
    catch (JSException ex)
    {
      Console.Error.WriteLine($"[module {id}] deactivation failed: {ex.Message}");
    }

    _active.Remove(id);
  }

  /// <summary>A packaged file's URL on the server and its integrity ("sha256-..."), when the manifest serves it.</summary>
  private (string Url, string Integrity)? Served(ModuleDescriptor d, string path)
  {
    if (d.AssetBase is null || d.AssetHashes is null || ModuleAssetSet.IsUrl(path)) return null;
    var normalized = ModuleAssetSet.Normalize(path);
    var hash = d.AssetHashes.FirstOrDefault(h => h.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase)).Value;
    if (hash is null) return null;
    var relative = d.AssetBase + string.Join('/', normalized.Split('/').Select(Uri.EscapeDataString));
    var url = _webApi is null ? relative : new Uri(_webApi, relative).ToString();
    return (url, "sha256-" + Convert.ToBase64String(Convert.FromHexString(hash)));
  }

  private sealed record StyleSpec(string Id, string? Css, string? Href, string? Integrity);
  /// <param name="Path">The script's path in the package (blob mode), so its relative imports can be resolved.</param>
  /// <param name="Files">Blob mode: the package's other scripts (chunks) by path; moduleHost turns the ones the script
  /// imports into blobs too, since a blob has no neighbouring files to import from.</param>
  private sealed record ScriptSpec(string Id, string? Code, string? Url, string? Integrity, string? Path = null,
    Dictionary<string, string>? Files = null);

  private static Dictionary<string, string>? OtherScripts(ModuleAssetSet assets, string entry)
  {
    var self = ModuleAssetSet.Normalize(entry);
    Dictionary<string, string>? files = null;
    foreach (var path in assets.Paths)
    {
      if (!path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.Equals(self, StringComparison.OrdinalIgnoreCase)) continue;
      if (assets.TryGetText(path, out var code)) (files ??= new(StringComparer.Ordinal))[path] = code;
    }
    return files;
  }
}
