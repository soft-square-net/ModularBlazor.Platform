using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using soft.square.net.ModularBlazor.Abstractions.Menus;
using soft.square.net.ModularBlazor.BlzWebClient.Auth;
using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// The menu badges (IMenuBadges). Entries with a Badge url (MenuLink / MenuGroup, Badge = "api/...") get theirs from
/// the module's own API: the host asks each url once for every entry using it, with the user's token, then again every
/// BadgeRefresh seconds (default 60) while the app is open. A url that fails (403, 404, an error) is asked again after
/// ten minutes and shows no badge meanwhile. Loaded module code sets badges or asks for a refresh through IMenuBadges.
/// Answers belong to the user: signing in or out drops them.
/// </summary>
public sealed class MenuBadgeService : IMenuBadges, IDisposable
{
  private const int DefaultSeconds = 60;
  private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);
  private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(10);

  private readonly IHttpClientFactory _httpClients;
  private readonly AuthenticationStateProvider _auth;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly Timer _timer;

  private Dictionary<string, (string Url, int Seconds)> _sources = new(StringComparer.OrdinalIgnoreCase); // entry → its url
  private readonly Dictionary<string, MenuBadge?> _answers = new(StringComparer.OrdinalIgnoreCase);       // url → badge
  private readonly Dictionary<string, DateTimeOffset> _due = new(StringComparer.OrdinalIgnoreCase);        // url → next ask
  private readonly Dictionary<string, MenuBadge?> _set = new(StringComparer.OrdinalIgnoreCase);           // entry → set by code
  private string? _caller;

  public MenuBadgeService(IHttpClientFactory httpClients, AuthenticationStateProvider auth)
  {
    _httpClients = httpClients;
    _auth = auth;
    _timer = new Timer(_ => _ = AskDueAsync(), null, Tick, Tick);
  }

  public event Action? Changed;

  public MenuBadge? Of(string entryId)
  {
    if (_set.TryGetValue(entryId, out var set)) return set;
    return _sources.TryGetValue(entryId, out var source) ? _answers.GetValueOrDefault(source.Url) : null;
  }

  public void Set(string entryId, MenuBadge? badge)
  {
    if (_set.TryGetValue(entryId, out var current) && current == badge) return;
    _set[entryId] = badge;
    Changed?.Invoke();
  }

  public async Task RefreshAsync(string entryId)
  {
    if (!_sources.TryGetValue(entryId, out var source)) return;
    _due[source.Url] = DateTimeOffset.MinValue;
    await AskDueAsync();
  }

  /// <summary>The entries of the menus as they are now built (MenuService calls it on every rebuild).</summary>
  public void Track(IEnumerable<MenuItem> items)
  {
    var sources = items.Where(i => !string.IsNullOrWhiteSpace(i.Badge))
        .ToDictionary(i => i.Id, i => (Url: i.Badge!, Seconds: i.BadgeRefresh > 0 ? i.BadgeRefresh : DefaultSeconds), StringComparer.OrdinalIgnoreCase);
    var changed = sources.Count != _sources.Count || sources.Any(s => !_sources.TryGetValue(s.Key, out var old) || old != s.Value);
    _sources = sources;
    if (!changed) return;

    foreach (var url in _due.Keys.Except(sources.Values.Select(s => s.Url), StringComparer.OrdinalIgnoreCase).ToList())
    {
      _due.Remove(url);
      _answers.Remove(url);
    }
    foreach (var url in sources.Values.Select(s => s.Url).Where(u => !_due.ContainsKey(u)))
      _due[url] = DateTimeOffset.MinValue;
    _ = AskDueAsync();
  }

  /// <summary>Asks every url whose time has come; one request per url, whatever the number of entries using it.</summary>
  private async Task AskDueAsync()
  {
    if (!await _gate.WaitAsync(0)) return; // a round is running; it will see what is due
    try
    {
      var user = (await _auth.GetAuthenticationStateAsync()).User;
      var signedIn = user.Identity?.IsAuthenticated == true;
      var caller = signedIn ? $"{user.FindFirst("sub")?.Value}|{user.FindFirst("tenant")?.Value}" : "";
      var changed = false;
      if (caller != _caller)
      {
        _caller = caller;
        changed = _answers.Count > 0 || _set.Count > 0;
        _answers.Clear();
        _set.Clear();
        foreach (var url in _due.Keys.ToList()) _due[url] = DateTimeOffset.MinValue;
      }

      var now = DateTimeOffset.UtcNow;
      var due = _due.Where(d => d.Value <= now).Select(d => d.Key).ToList();
      if (due.Count > 0)
      {
        var http = _httpClients.CreateClient(signedIn ? AuthServiceExtensions.WebApiClient : AuthServiceExtensions.WebApiPublicClient);
        foreach (var url in due)
        {
          var (badge, ok) = await AskAsync(http, url);
          var seconds = _sources.Values.Where(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase)).Select(s => s.Seconds).DefaultIfEmpty(DefaultSeconds).Min();
          _due[url] = DateTimeOffset.UtcNow + (ok ? TimeSpan.FromSeconds(seconds) : AfterFailure);
          // A fresh answer replaces what code set for the entries of this url.
          foreach (var id in _sources.Where(s => s.Value.Url.Equals(url, StringComparison.OrdinalIgnoreCase)).Select(s => s.Key))
            changed |= _set.Remove(id);
          if (_answers.TryGetValue(url, out var old) && old == badge) continue;
          _answers[url] = badge;
          changed = true;
        }
      }
      if (changed) Changed?.Invoke();
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Menu badges: {ex.Message}");
    }
    finally
    {
      _gate.Release();
    }
  }

  private static async Task<(MenuBadge? Badge, bool Ok)> AskAsync(HttpClient http, string url)
  {
    try
    {
      using var response = await http.GetAsync(url);
      if (response.StatusCode is HttpStatusCode.NoContent) return (null, true);
      if (!response.IsSuccessStatusCode) return (null, false);
      using var answer = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
      return (MenuBadge.FromJson(answer.RootElement), true);
    }
    catch (AccessTokenNotAvailableException)
    {
      return (null, false);
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
    {
      return (null, false);
    }
  }

  public void Dispose() => _timer.Dispose();
}
