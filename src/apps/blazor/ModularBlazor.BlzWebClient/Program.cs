using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using soft.square.net.ModularBlazor.BlzWebClient;
using soft.square.net.ModularBlazor.BlzWebClient.Auth;
using soft.square.net.ModularBlazor.BlzWebClient.Services;
using soft.square.net.ModularBlazor.Abstractions.MudTail;
using Microsoft.Extensions.Localization;
using soft.square.net.ModularBlazor.Abstractions.Localization;
using soft.square.net.Share.All.Localization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Two kinds of HttpClient, on purpose:
//   * "LocalHttpClient" (this site's own origin): app files; modules come from the WebApi, not from this site.
//   * "WebApi" (the YARP gateway in front of every WebApi pool): carries the OpenIddict access token;
//     "WebApi.Public" is the same gateway without a token (the anonymous client manifest and public module files).
builder.Services.AddKeyedSingleton<HttpClient>("LocalHttpClient", (_, _) => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

// Sign-in: OpenID Connect against the OpenIddict identity service (wwwroot/appsettings.json "Oidc").
builder.Services.AddOpenIddictAuthentication(builder.Configuration);

// The gateway (wwwroot/appsettings.json "WebApi:BaseUrl"): every call gets the user's access token.
var gateway = builder.Configuration["WebApi:BaseUrl"];
if (string.IsNullOrWhiteSpace(gateway)) gateway = "http://localhost:5080/";
builder.Services.AddWebApiClient(gateway);
builder.Services.AddScoped<HostModulesApi>();

// MudTail's runtime, shared through the UI.MudTail module (tables, toasts, behaviours on this app's pages).
builder.Services.AddScoped<MudTailRuntime>();

// Languages (Arabic by default, see wwwroot/appsettings.json "Localization"). Texts: IStringLocalizer<T>, read from the
// i18n/{culture}.json files embedded in each assembly, with the shared words of ModularBlazor.Abstractions behind them.
// Every HttpClient sends the language, so the WebApi answers in it too.
builder.Services.AddSingleton<IStringLocalizerFactory>(new EmbeddedJsonStringLocalizerFactory(typeof(ILocaleService).Assembly));
builder.Services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
builder.Services.AddScoped<LocaleService>();
builder.Services.AddScoped<ILocaleService>(sp => sp.GetRequiredService<LocaleService>());
builder.Services.AddTransient<AcceptLanguageHandler>();
builder.Services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler<AcceptLanguageHandler>());

// Modules. ModuleManager and ActiveModuleService follow the user's session (AuthenticationStateProvider is scoped),
// so they are scoped too; WebAssembly has one scope for the whole app, so they still live as long as the page.
builder.Services.AddSingleton(new ModuleOptions());
builder.Services.AddSingleton<ModuleAssetStore>();
builder.Services.AddSingleton<ModuleCache>();
builder.Services.AddSingleton<IModulePackageReader, DllPackageReader>();
builder.Services.AddSingleton<IModulePackageReader, NuPkgPackageReader>();
builder.Services.AddScoped<ModuleManager>();
builder.Services.AddScoped<ActiveModuleService>();
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.IModuleNavigation>(sp => sp.GetRequiredService<ModuleManager>());
// The menus (main, secondary, user) the modules' MenuGroup / MenuLink attributes fill, shown by <MenuZone> in layouts.
builder.Services.AddScoped<MenuCustomizations>();
builder.Services.AddScoped<MenuBadgeService>();
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.Menus.IMenuBadges>(sp => sp.GetRequiredService<MenuBadgeService>());
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.Menus.IMenuService>(sp => sp.GetRequiredService<MenuService>());
// Dialogs one module opens in another by id (IModuleDialogs), shown by Components/ModuleDialogHost in App.razor.
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.Lookups.ILookupService, soft.square.net.ModularBlazor.BlzWebClient.Services.LookupService>();
builder.Services.AddScoped<ModuleDialogService>();
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.IModuleDialogs>(sp => sp.GetRequiredService<ModuleDialogService>());
// The layout contract (Abstractions/Shell): page context, module contributions (header actions, docks, context
// panels), the tenant's brand (branding core module) and the user's permissions (identity core module's api/identity/me).
builder.Services.AddScoped<PageContextService>();
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.Shell.IPageContext>(sp => sp.GetRequiredService<PageContextService>());
builder.Services.AddScoped<ShellContributionService>();
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.Shell.IShellContributions>(sp => sp.GetRequiredService<ShellContributionService>());
builder.Services.AddScoped<BrandingService>();
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.Shell.IBranding>(sp => sp.GetRequiredService<BrandingService>());
builder.Services.AddScoped<UserPermissionsService>();
builder.Services.AddScoped<soft.square.net.ModularBlazor.Abstractions.Shell.IUserPermissions>(sp => sp.GetRequiredService<UserPermissionsService>());
// Development only: a module repo's playground reloads a rebuilt client module in place (see DevModuleReloader).
builder.Services.AddScoped<DevModuleReloader>();

var host = builder.Build();

// host.Services is the app's scope (the one components resolve from), so these are the instances the UI sees.
var modules = host.Services.GetRequiredService<ModuleManager>();

// Phase 1: the client manifest from the WebApi (api/client-modules/manifest; the last anonymous one this browser
// received when it cannot be reached), then the startup modules (layouts, MudTail theme) before the first render. A returning
// user's OIDC session is still in the browser, so their manifest is the one fetched and their modules are known before
// the first route.
await modules.LoadStartupModulesAsync();

// ...and the startup modules' styles/scripts go live (they stay active for the whole session).
await host.Services.GetRequiredService<ActiveModuleService>().SyncAsync();

// The tenant's brand over MudTail's defaults (index.html already painted the cached one), then the server's.
await host.Services.GetRequiredService<BrandingService>().InitializeAsync();

// The language, before the first render: the saved choice or the default (MudTail is active by now, so it follows).
await host.Services.GetRequiredService<LocaleService>().InitializeAsync();

// Development: listen for rebuilt local client modules (the WebApi answers 404 when it has none, and this stops).
if (builder.HostEnvironment.IsDevelopment()) host.Services.GetRequiredService<DevModuleReloader>().Start();

await host.RunAsync();
