using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using soft.square.net.ModularBlazor.Abstractions.Shell;
using soft.square.net.ModularBlazor.BlzWebClient.Auth;
using soft.square.net.Share.All;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// The tenant's brand (IBranding), from the branding core module's <c>GET api/branding</c>.
/// <list type="bullet">
/// <item>The last brand this browser received is kept in localStorage ("mb.brand"); index.html's <c>appBrand</c>
/// applies it before anything renders (colours, radius, font, colour mode, favicon), so a returning user never sees
/// the default brand first.</item>
/// <item><see cref="InitializeAsync"/> runs before the first render, after the startup modules (MudTail) are active:
/// it applies the cached brand again over MudTail's defaults, then asks the server with the cached ETag.</item>
/// <item>Fetched again whenever the module catalog changes (sign-in, sign-out, another tenant).</item>
/// </list>
/// Images come as urls on the WebApi (absolute here, so layouts can put them in &lt;img src&gt; as they are).
/// Without the branding module the platform's own brand stays.
/// </summary>
public sealed class BrandingService : IBranding, IDisposable
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  private readonly ModuleManager _modules;
  private readonly IHttpClientFactory _httpClients;
  private readonly AuthenticationStateProvider _auth;
  private readonly IJSRuntime _js;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private string? _etag;
  private string? _etagCaller; // whose brand _etag is: "" signed out, otherwise the tenant

  public BrandingService(ModuleManager modules, IHttpClientFactory httpClients, AuthenticationStateProvider auth, IJSRuntime js)
  {
    _modules = modules;
    _httpClients = httpClients;
    _auth = auth;
    _js = js;
    _modules.Changed += OnCatalogChanged;
  }

  public BrandInfo Current { get; private set; } = BrandInfo.Default;

  public event Action? Changed;

  /// <summary>Before the first render: the cached brand, applied over MudTail's defaults, then the server's.</summary>
  public async Task InitializeAsync()
  {
    try
    {
      if (await _js.InvokeAsync<Cached?>("appBrand.get") is { Brand: { } brand } cached)
      {
        (_etag, _etagCaller) = (cached.ETag, cached.Caller);
        Set(ToInfo(brand));
        await _js.InvokeVoidAsync("appBrand.apply", brand, null, null);
      }
    }
    catch (Exception ex) when (ex is JSException or JsonException)
    {
      Console.Error.WriteLine($"Branding: the cached brand could not be read ({ex.Message}).");
    }
    await RefreshAsync();
  }

  private void OnCatalogChanged() => _ = RefreshAsync();

  /// <summary>Asks the server for the brand (304 keeps the current one) and applies a new one.</summary>
  public async Task RefreshAsync()
  {
    await _gate.WaitAsync();
    try
    {
      var user = (await _auth.GetAuthenticationStateAsync()).User;
      var signedIn = user.Identity?.IsAuthenticated == true;
      var caller = signedIn ? $"t:{user.FindFirst(Abstractions.Auth.UserInfo.TenantClaimType)?.Value}" : "";
      if (caller != _etagCaller) _etag = null; // another tenant's brand: ask for the whole one

      var client = _httpClients.CreateClient(signedIn ? AuthServiceExtensions.WebApiClient : AuthServiceExtensions.WebApiPublicClient);
      using var request = new HttpRequestMessage(HttpMethod.Get, "api/branding");
      if (_etag is not null) request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{_etag}\""));
      using var response = await client.SendAsync(request);
      if (response.StatusCode == HttpStatusCode.NotModified || !response.IsSuccessStatusCode) return; // 404: no branding module

      var brand = Absolute(await response.Content.ReadFromJsonAsync<JsonElement>(Json), client.BaseAddress);
      (_etag, _etagCaller) = (brand.TryGetProperty("eTag", out var etag) ? etag.GetString() : null, caller);
      try { await _js.InvokeVoidAsync("appBrand.apply", brand, _etag, caller); }
      catch (JSException ex) { Console.Error.WriteLine($"Branding: the brand could not be applied ({ex.Message})."); }
      Set(ToInfo(brand));
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException
                                  or Microsoft.AspNetCore.Components.WebAssembly.Authentication.AccessTokenNotAvailableException)
    {
      Console.Error.WriteLine($"Branding: could not read the brand ({ex.Message}).");
    }
    finally
    {
      _gate.Release();
    }
  }

  private void Set(BrandInfo info)
  {
    if (info == Current) return;
    Current = info;
    Changed?.Invoke();
  }

  /// <summary>The server's brand with its image urls made absolute on the WebApi (they are relative to it).</summary>
  private static JsonElement Absolute(JsonElement brand, Uri? baseAddress)
  {
    var node = System.Text.Json.Nodes.JsonNode.Parse(brand.GetRawText())!.AsObject();
    foreach (var name in new[] { "logoUrl", "logoDarkUrl", "faviconUrl" })
      if (node[name]?.GetValue<string>() is { Length: > 0 } url && baseAddress is not null && !Uri.IsWellFormedUriString(url, UriKind.Absolute))
        node[name] = new Uri(baseAddress, url).ToString();
    return JsonSerializer.SerializeToElement(node, Json);
  }

  private static BrandInfo ToInfo(JsonElement brand)
  {
    var dto = brand.Deserialize<BrandDto>(Json);
    if (dto is null || string.IsNullOrWhiteSpace(dto.Name)) return BrandInfo.Default;
    return new BrandInfo(dto.Name, dto.ShortName, dto.LogoUrl, dto.LogoDarkUrl, dto.FaviconUrl, dto.Primary, dto.Secondary,
      dto.Radius, dto.Font, dto.DefaultMode ?? ThemeModes.System, dto.AllowedModes, dto.AllowUserColors ?? true);
  }

  public void Dispose() => _modules.Changed -= OnCatalogChanged;

  /// <summary>What appBrand.get answers: the last brand received, its ETag and whose it is.</summary>
  private sealed record Cached(string? ETag, string? Caller, JsonElement? Brand);

  /// <summary>The branding module's EffectiveBrand, as far as the client reads it.</summary>
  private sealed record BrandDto(
    string? Name, string? ShortName, string? LogoUrl, string? LogoDarkUrl, string? FaviconUrl, string? Primary,
    string? Secondary, int? Radius, string? Font, string? DefaultMode, IReadOnlyList<string>? AllowedModes, bool? AllowUserColors);
}
