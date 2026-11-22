using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Localization;
using soft.square.net.ModularBlazor.Abstractions;
using soft.square.net.ModularBlazor.Abstractions.Menus;
using soft.square.net.ModularBlazor.Abstractions.MudTail;
using soft.square.net.WebApi.Modules.Core.Tenancy.Client;

namespace soft.square.net.Module.Tenants;

/// <summary>
/// What both pages share: the Tenancy client on the host's "WebApi" HttpClient (the gateway, with the OpenIddict access
/// token), one-call-at-a-time state, readable error messages, and the date conventions of subscriptions.
/// </summary>
public abstract class TenancyPageBase : ComponentBase
{
    [Inject] protected IHttpClientFactory HttpClients { get; set; } = default!;
    [Inject] protected MudTailRuntime MudTail { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected IStringLocalizerFactory Texts { get; set; } = default!;
    [Inject] protected IMenuBadges Badges { get; set; } = default!;

    /// <summary>This module's Tenants menu link (ModuleInfo.cs), whose badge counts the tenants that need attention.</summary>
    protected const string MenuEntry = "Module.Tenants:tenants";

    private IStringLocalizer? _l;

    /// <summary>The texts of this module (its embedded i18n/{culture}.json), English keys.</summary>
    protected IStringLocalizer L => _l ??= Texts.Create(GetType());

    /// <summary>Tenancy routes (/api/tenancy/...) reach the core pool through the gateway's catch-all route.</summary>
    protected TenancyClient Tenancy => new(HttpClients.CreateClient(HttpClientNames.WebApi));

    protected bool Busy { get; private set; }
    protected string? Error { get; set; }

    /// <summary>Runs one API call: busy flag, error message and toast, and <paramref name="then"/> when it succeeded.</summary>
    protected async Task<bool> RunAsync(Func<Task> call, Func<Task>? then = null)
    {
        Busy = true;
        Error = null;
        StateHasChanged();
        try
        {
            await call();
        }
        catch (AccessTokenNotAvailableException ex)
        {
            ex.Redirect(); // the session ended: sign in again and come back
            return false;
        }
        catch (Exception ex)
        {
            Error = Describe(ex);
            await MudTail.ToastAsync(Error, "error");
            return false;
        }
        finally
        {
            Busy = false;
            StateHasChanged();
        }

        if (then is not null) await then();
        StateHasChanged();
        _ = Badges.RefreshAsync(MenuEntry); // a change here may change the badge's count
        return true;
    }

    protected string Describe(Exception ex) => ex switch
    {
        TenancyApiException<HttpValidationProblemDetails> v =>
            $"{v.StatusCode}: {v.Result.Detail ?? v.Result.Title ?? L["invalid request"]}"
            + (v.Result.Errors is { Count: > 0 } errors ? " " + string.Join(" ", errors.SelectMany(e => e.Value)) : ""),
        TenancyApiException<ProblemDetails> p => $"{p.StatusCode}: {p.Result.Detail ?? p.Result.Title ?? L["request failed"]}",
        TenancyApiException { StatusCode: 401 } => L["401: the gateway did not accept the access token. Sign in again."],
        TenancyApiException { StatusCode: 403 } => L["403: your account may not manage tenants (platform.tenants.manage)."],
        TenancyApiException { StatusCode: 503 } => L["503: the tenant store is not configured on the WebApi."],
        TenancyApiException api => $"{api.StatusCode}: {FirstLine(api.Response) ?? FirstLine(api.Message)}",
        HttpRequestException http => L["The gateway is not reachable ({0}).", http.Message],
        _ => ex.Message,
    };

    private static string? FirstLine(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Split('\n', 2)[0].Trim();

    // ------------------------------------------------------------ dates
    // A subscription covers [StartsAt, EndsAt): EndsAt is exclusive. The forms ask for the first and the last day
    // (inclusive, UTC), so "1 Jan to 31 Dec" is stored as StartsAt 1 Jan 00:00Z and EndsAt 1 Jan of the next year.

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    protected static DateTimeOffset StartOf(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    protected static DateTimeOffset EndAfter(DateOnly lastDay) => StartOf(lastDay.AddDays(1));

    protected static DateOnly FirstDay(DateTimeOffset startsAt) => DateOnly.FromDateTime(startsAt.UtcDateTime);

    protected static DateOnly LastDay(DateTimeOffset endsAt) => DateOnly.FromDateTime(endsAt.UtcDateTime.AddTicks(-1));

    protected static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd");
}
