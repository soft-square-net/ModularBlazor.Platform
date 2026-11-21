using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using soft.square.net.ModularBlazor.Abstractions.Localization;
using soft.square.net.ModularBlazor.Abstractions.MudTail;
using soft.square.net.Share.All.Localization;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// The user's language (see <see cref="ILocaleService"/>). Settings, in wwwroot/appsettings.json:
/// <code>"Localization": { "DefaultCulture": "ar", "Cultures": [ "ar", "en" ] }</code>
/// The choice is kept in localStorage ("mb.culture"); index.html applies it to &lt;html lang dir&gt; before Blazor
/// starts, so the page never flips direction while loading.
/// </summary>
public sealed class LocaleService : ILocaleService
{
    public const string DefaultCulture = "ar";

    private readonly IJSRuntime _js;
    private readonly MudTailRuntime _mudTail;
    private readonly string _default;

    public LocaleService(IConfiguration configuration, IJSRuntime js, MudTailRuntime mudTail)
    {
        _js = js;
        _mudTail = mudTail;
        var cultures = configuration.GetSection("Localization:Cultures").Get<string[]>() is { Length: > 0 } configured ? configured : [DefaultCulture, "en"];
        _default = Offered(configuration["Localization:DefaultCulture"], cultures) ?? Offered(DefaultCulture, cultures) ?? cultures[0];
        Available = cultures
            .Select(c => CultureInfo.GetCultureInfo(c.Trim()))
            .DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c.Name.Equals(_default, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(c => new LocaleInfo(c.Name, NativeName(c), TextDirection.IsRightToLeft(c.Name)))
            .ToList();
        Culture = _default;
    }

    public string Culture { get; private set; }
    public bool IsRightToLeft => TextDirection.IsRightToLeft(Culture);
    public IReadOnlyList<LocaleInfo> Available { get; }
    public event Action? Changed;

    /// <summary>Before the first render: the saved choice, else the default.</summary>
    public async Task InitializeAsync()
    {
        string? saved = null;
        try { saved = await _js.InvokeAsync<string?>("appLocale.get"); }
        catch (JSException) { /* storage blocked: the default */ }
        await ApplyAsync(Offered(saved, Available.Select(a => a.Culture)) ?? _default, persist: false);
    }

    public async Task SetCultureAsync(string culture)
    {
        var offered = Offered(culture, Available.Select(a => a.Culture));
        if (offered is null || offered.Equals(Culture, StringComparison.OrdinalIgnoreCase)) return;
        await ApplyAsync(offered, persist: true);
        Changed?.Invoke();
    }

    private async Task ApplyAsync(string culture, bool persist)
    {
        Culture = culture;
        var info = CultureInfo.GetCultureInfo(culture);
        // WebAssembly runs on one thread: setting both the defaults and the current values covers every component.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = info;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = info;

        try { await _js.InvokeVoidAsync("appLocale.apply", culture, TextDirection.Of(culture), persist); }
        catch (JSException e) { Console.Error.WriteLine($"Could not apply the language: {e.Message}"); }

        // MudTail's own texts (toasts, tables, pickers) and its direction-aware behaviours.
        try { if (await _mudTail.GetAsync() is { } runtime) await runtime.InvokeVoidAsync("setLocale", culture); }
        catch (JSException e) { Console.Error.WriteLine($"MudTail did not take the language: {e.Message}"); }
    }

    private static string? Offered(string? culture, IEnumerable<string> cultures) =>
        string.IsNullOrWhiteSpace(culture) ? null : cultures.FirstOrDefault(c => c.Trim().Equals(culture.Trim(), StringComparison.OrdinalIgnoreCase))?.Trim();

    private static string NativeName(CultureInfo culture)
    {
        var name = culture.NativeName;
        // Invariant-globalization builds return the English name; keep the two the app ships readable anyway.
        return culture.TwoLetterISOLanguageName switch
        {
            "ar" when !name.Any(ch => ch is >= '؀' and <= 'ۿ') => "العربية",
            _ => name.Length > 0 ? char.ToUpper(name[0], culture) + name[1..] : culture.Name
        };
    }
}

/// <summary>Sends the user's language with every API call, so the WebApi answers (errors, messages) in it.</summary>
public sealed class AcceptLanguageHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        if (culture.Length > 0)
        {
            request.Headers.AcceptLanguage.Clear();
            request.Headers.AcceptLanguage.ParseAdd(culture);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
