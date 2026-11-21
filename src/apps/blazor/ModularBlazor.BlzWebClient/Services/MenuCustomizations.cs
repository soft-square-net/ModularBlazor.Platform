using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using soft.square.net.ModularBlazor.BlzWebClient.Auth;
using soft.square.net.Share.All;
using soft.square.net.WebApi.Modules.Core.Menus.Client;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// The administrators' changes to the menus (the WebApi's "menus" core module, GET api/menus): fetched for whoever is
/// signed in (and their tenant) whenever the module catalog changes, at most every half minute unless forced (the
/// menus admin page forces it after a save). Without the module, or when it cannot be reached, the menus are the
/// modules' own.
/// </summary>
public sealed class MenuCustomizations : IDisposable
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

  private readonly ModuleManager _modules;
  private readonly IHttpClientFactory _httpClients;
  private readonly AuthenticationStateProvider _auth;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private string? _etag;
  private string? _caller;         // who the current answer is for ("" anonymous)
  private DateTimeOffset _fetched;

  public MenuCustomizations(ModuleManager modules, IHttpClientFactory httpClients, AuthenticationStateProvider auth)
  {
    _modules = modules;
    _httpClients = httpClients;
    _auth = auth;
    _modules.Changed += OnCatalogChanged;
    // Created when a layout first shows a menu, usually after the catalog was loaded: fetch now, not on its next change.
    _ = RefreshAsync(force: false);
  }

  /// <summary>The changes for the current caller; null until fetched, or when the WebApi has no menus module.</summary>
  public EffectiveMenus? Current { get; private set; }

  /// <summary>The current caller may change the menus (show the admin page).</summary>
  public bool CanManage => Current?.CanManage == true;

  public event Action? Changed;

  private void OnCatalogChanged() => _ = RefreshAsync(force: false);

  /// <summary>Fetches the changes again (force: now, even when fetched a moment ago).</summary>
  public async Task RefreshAsync(bool force = true)
  {
    await _gate.WaitAsync();
    try
    {
      var user = (await _auth.GetAuthenticationStateAsync()).User;
      var signedIn = user.Identity?.IsAuthenticated == true;
      var caller = signedIn ? $"{user.FindFirst("sub")?.Value}|{user.FindFirst("tenant")?.Value}" : "";
      var same = caller == _caller;
      if (!force && same && DateTimeOffset.UtcNow - _fetched < MinInterval) return;

      var client = _httpClients.CreateClient(signedIn ? AuthServiceExtensions.WebApiClient : AuthServiceExtensions.WebApiPublicClient);
      using var request = new HttpRequestMessage(HttpMethod.Get, "api/menus");
      if (same && _etag is not null && EntityTagHeaderValue.TryParse(_etag, out var etag)) request.Headers.IfNoneMatch.Add(etag);
      using var response = await client.SendAsync(request);
      _fetched = DateTimeOffset.UtcNow;
      if (response.StatusCode == HttpStatusCode.NotModified) return;

      EffectiveMenus? menus = null;
      if (response.IsSuccessStatusCode) menus = await response.Content.ReadFromJsonAsync<EffectiveMenus>(Json);
      else if (response.StatusCode != HttpStatusCode.NotFound) // 404: the WebApi has no menus module; anything else: keep what we have
      {
        Console.Error.WriteLine($"Menus: GET api/menus answered {(int)response.StatusCode}; the menus are shown without the administrators' changes.");
        if (same) return;
      }
      (Current, _etag, _caller) = (menus, response.Headers.ETag?.ToString(), caller);
      Changed?.Invoke();
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException
                                  or Microsoft.AspNetCore.Components.WebAssembly.Authentication.AccessTokenNotAvailableException)
    {
      Console.Error.WriteLine($"Menus: could not read the administrators' changes ({ex.Message}).");
    }
    finally
    {
      _gate.Release();
    }
  }

  public void Dispose() => _modules.Changed -= OnCatalogChanged;

  /// <summary>
  /// The modules' entries with the administrators' changes: their added items joined, then per entry id hidden (with
  /// everything in it), moved to another zone or group, reordered, renamed. Pure: public for tests.
  /// </summary>
  public static List<MenuItem> Apply(IReadOnlyList<MenuItem> items, EffectiveMenus? menus)
  {
    if (menus is null) return items.ToList();

    var all = items.Concat((menus.Items ?? []).Where(e => !string.IsNullOrEmpty(e.Id)).Select(e => new MenuItem(
            e.Id!, e.Kind ?? MenuItemKinds.Link, e.Zone ?? MenuZones.Main, e.Title ?? e.Id!, e.Href, e.Parent, e.Icon, e.Order,
            e.Titles is { Count: > 0 } t ? new Dictionary<string, string>(t, StringComparer.OrdinalIgnoreCase) : null, e.External)))
        .DistinctBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var overrides = (menus.Overrides ?? []).Where(o => !string.IsNullOrEmpty(o.Id))
        .GroupBy(o => o.Id!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    var changed = all.Select(item =>
    {
      if (!overrides.TryGetValue(item.Id, out var o)) return item;
      return item with
      {
        Zone = string.IsNullOrWhiteSpace(o.Zone) ? item.Zone : o.Zone,
        Parent = o.Parent is null ? item.Parent : o.Parent.Length == 0 ? null : o.Parent,
        Order = o.Order ?? item.Order,
        // A new title replaces the translations too: they were of the old one.
        Title = string.IsNullOrWhiteSpace(o.Title) ? item.Title : o.Title,
        Titles = !string.IsNullOrWhiteSpace(o.Title) || o.Titles is { Count: > 0 }
            ? (o.Titles is { Count: > 0 } t ? new Dictionary<string, string>(t, StringComparer.OrdinalIgnoreCase) : null)
            : item.Titles,
        Icon = string.IsNullOrWhiteSpace(o.Icon) ? item.Icon : o.Icon
      };
    }).ToList();

    // Hidden: the entry and what is in it (its children follow their group, wherever the group went).
    var hidden = overrides.Values.Where(o => o.Hidden == true).Select(o => o.Id!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    for (var grew = true; grew;)
    {
      grew = false;
      foreach (var item in changed.Where(i => i.Parent is { } p && hidden.Contains(p) && !hidden.Contains(i.Id)))
        grew |= hidden.Add(item.Id);
    }
    return changed.Where(i => !hidden.Contains(i.Id)).ToList();
  }
}
