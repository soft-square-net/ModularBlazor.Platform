using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using soft.square.net.Share.All.Localization;

namespace soft.square.net.WebApi.Localization;

/// <summary>
/// Languages of the WebApi: every request runs in the caller's culture (Accept-Language, or ?culture=ar), one of
/// Localization:Cultures (default ar, en), falling back to Localization:DefaultCulture (default ar).
/// <para>
/// Texts come from each assembly's embedded i18n/{culture}.json (keys are the English texts), so a runtime module
/// brings its own translations in its dll and works in any pool. <c>IStringLocalizer&lt;T&gt;</c> for a module type is
/// built in the module's container from the host's open-generic registration, and the factory keeps the parsed
/// texts in a weak table, so a module's load context can still unload.
/// </para>
/// </summary>
public static class PlatformLocalization
{
    public static IServiceCollection AddPlatformLocalization(this IServiceCollection services, IConfiguration configuration)
    {
        var options = Read(configuration);
        services.AddSingleton<IStringLocalizerFactory>(new EmbeddedJsonStringLocalizerFactory());
        services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
        services.Configure<RequestLocalizationOptions>(o =>
        {
            var cultures = options.Cultures.Select(c => new CultureInfo(c)).ToList();
            o.DefaultRequestCulture = new RequestCulture(options.DefaultCulture);
            o.SupportedCultures = cultures;
            o.SupportedUICultures = cultures;
            o.FallBackToParentCultures = true;
            o.FallBackToParentUICultures = true;
            // ?culture=… first (links, tests), then Accept-Language (the clients send it on every call). No cookie:
            // the WebApi is called with a bearer token, never with the browser's cookies.
            o.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
            o.ApplyCurrentCultureToResponseHeaders = true; // Content-Language
        });
        return services;
    }

    public static IApplicationBuilder UsePlatformLocalization(this IApplicationBuilder app) => app.UseRequestLocalization();

    private static (string DefaultCulture, string[] Cultures) Read(IConfiguration configuration)
    {
        var defaultCulture = configuration["Localization:DefaultCulture"] is { Length: > 0 } d ? d : "ar";
        var cultures = configuration.GetSection("Localization:Cultures").Get<string[]>() is { Length: > 0 } c ? c : ["ar", "en"];
        if (!cultures.Contains(defaultCulture, StringComparer.OrdinalIgnoreCase)) cultures = [defaultCulture, .. cultures];
        return (defaultCulture, cultures);
    }
}
