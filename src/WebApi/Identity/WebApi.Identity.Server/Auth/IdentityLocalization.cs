using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using soft.square.net.Share.All.Localization;

namespace soft.square.net.WebApi.Identity.Server.Auth;

/// <summary>
/// Languages of the sign-in pages: Arabic by default (Localization:DefaultCulture), English as well
/// (Localization:Cultures). The language comes from, in order:
/// <list type="number">
///   <item>?culture=en (the language links on every page);</item>
///   <item>the OIDC <c>ui_locales</c> parameter of the authorization request, also when it sits in ?ReturnUrl=… of the
///   login page, so a page opened by an app shows the app's language;</item>
///   <item>the culture cookie, written whenever 1 or 2 chose a language.</item>
/// </list>
/// The browser's Accept-Language is not used, like the Blazor client: the platform's default is Arabic.
/// Texts: i18n/{culture}.json embedded in this assembly, keys are the English texts.
/// </summary>
public static class IdentityLocalization
{
    public static IServiceCollection AddIdentityLocalization(this IServiceCollection services, IConfiguration configuration)
    {
        var defaultCulture = configuration["Localization:DefaultCulture"] is { Length: > 0 } d ? d : "ar";
        var cultures = configuration.GetSection("Localization:Cultures").Get<string[]>() is { Length: > 0 } c ? c : ["ar", "en"];
        if (!cultures.Contains(defaultCulture, StringComparer.OrdinalIgnoreCase)) cultures = [defaultCulture, .. cultures];

        services.AddSingleton<IStringLocalizerFactory>(new EmbeddedJsonStringLocalizerFactory());
        services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
        services.Configure<RequestLocalizationOptions>(o =>
        {
            var supported = cultures.Select(x => new CultureInfo(x)).ToList();
            o.DefaultRequestCulture = new RequestCulture(defaultCulture);
            o.SupportedCultures = supported;
            o.SupportedUICultures = supported;
            o.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new CustomRequestCultureProvider(UiLocalesAsync),
                new CookieRequestCultureProvider(),
            ];
        });
        return services;
    }

    /// <summary>Request localization, and the cookie that keeps a language chosen by a link or by an app.</summary>
    public static IApplicationBuilder UseIdentityLocalization(this IApplicationBuilder app)
    {
        app.UseRequestLocalization();
        return app.Use(async (context, next) =>
        {
            var chosen = context.Features.Get<IRequestCultureFeature>();
            if (chosen?.Provider is QueryStringRequestCultureProvider or CustomRequestCultureProvider)
            {
                context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(chosen.RequestCulture),
                    new CookieOptions { MaxAge = TimeSpan.FromDays(365), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = true, Secure = context.Request.IsHttps });
            }
            await next();
        });
    }

    /// <summary>The first language of OIDC ui_locales (space separated), from the query or from the ReturnUrl's query.</summary>
    private static Task<ProviderCultureResult?> UiLocalesAsync(HttpContext context)
    {
        var value = context.Request.Query["ui_locales"].ToString();
        if (string.IsNullOrEmpty(value) && context.Request.Query["ReturnUrl"].ToString() is { Length: > 0 } returnUrl)
        {
            var q = returnUrl.IndexOf('?');
            if (q >= 0) value = QueryHelpers.ParseQuery(returnUrl[q..]).TryGetValue("ui_locales", out var v) ? v.ToString() : "";
        }
        var first = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return Task.FromResult(first is null ? null : new ProviderCultureResult(first));
    }
}
