using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using soft.square.net.ModularBlazor.Abstractions.Auth;
using soft.square.net.ModularBlazor.Abstractions.Shell;
using soft.square.net.ModularBlazor.BlzWebClient.Auth;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// The signed-in user's permissions in their tenant (IUserPermissions), from the identity core module's
/// <c>GET api/identity/me</c> (grants active now, plus platform roles). Fetched again whenever the module catalog
/// changes (sign-in, sign-out, and a grant that changes the user's menus changes the manifest), at most every half minute
/// unless the caller changed. Without the identity module, or signed out, nothing is loaded.
/// </summary>
public sealed class UserPermissionsService : IUserPermissions, IDisposable
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

  private readonly ModuleManager _modules;
  private readonly IHttpClientFactory _httpClients;
  private readonly AuthenticationStateProvider _auth;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private HashSet<string> _permissions = new(StringComparer.Ordinal);
  private string? _caller;
  private DateTimeOffset _fetched;

  public UserPermissionsService(ModuleManager modules, IHttpClientFactory httpClients, AuthenticationStateProvider auth)
  {
    _modules = modules;
    _httpClients = httpClients;
    _auth = auth;
    _modules.Changed += OnCatalogChanged;
    _ = RefreshAsync(force: false);
  }

  public bool IsLoaded { get; private set; }

  public event Action? Changed;

  public bool Has(string permission) => _permissions.Contains(permission);

  public bool HasAny(IEnumerable<string> permissions)
  {
    var any = false;
    foreach (var permission in permissions)
    {
      if (_permissions.Contains(permission)) return true;
      any = true;
    }
    return !any; // none asked for: allowed
  }

  private void OnCatalogChanged() => _ = RefreshAsync(force: false);

  /// <summary>Fetches the permissions again (force: now, even when fetched a moment ago).</summary>
  public async Task RefreshAsync(bool force = true)
  {
    await _gate.WaitAsync();
    try
    {
      var user = (await _auth.GetAuthenticationStateAsync()).User;
      var caller = user.Identity?.IsAuthenticated == true ? $"{user.FindFirst(UserInfo.UserIdClaimType)?.Value}|{user.FindFirst(UserInfo.TenantClaimType)?.Value}" : "";
      var same = caller == _caller;
      if (!force && same && DateTimeOffset.UtcNow - _fetched < MinInterval) return;

      if (caller.Length == 0)
      {
        Set(caller, [], loaded: false);
        return;
      }

      using var response = await _httpClients.CreateClient(AuthServiceExtensions.WebApiClient).GetAsync("api/identity/me");
      _fetched = DateTimeOffset.UtcNow;
      if (response.IsSuccessStatusCode)
      {
        var me = await response.Content.ReadFromJsonAsync<Me>(Json);
        Set(caller, me?.Permissions ?? [], loaded: true);
      }
      else if (response.StatusCode == HttpStatusCode.NotFound || !same)
      {
        // No identity module, or a new caller whose permissions are unknown: nothing granted, not loaded.
        Set(caller, [], loaded: false);
      }
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException
                                  or Microsoft.AspNetCore.Components.WebAssembly.Authentication.AccessTokenNotAvailableException)
    {
      Console.Error.WriteLine($"Permissions: could not read the user's permissions ({ex.Message}).");
    }
    finally
    {
      _gate.Release();
    }
  }

  private void Set(string caller, IEnumerable<string> permissions, bool loaded)
  {
    var next = new HashSet<string>(permissions, StringComparer.Ordinal);
    var changed = loaded != IsLoaded || !next.SetEquals(_permissions);
    (_permissions, IsLoaded, _caller) = (next, loaded, caller);
    if (changed) Changed?.Invoke();
  }

  public void Dispose() => _modules.Changed -= OnCatalogChanged;

  /// <summary>The part of the identity module's CurrentPrincipal this service reads.</summary>
  private sealed record Me(IReadOnlyList<string>? Permissions);
}
