using soft.square.net.ModularBlazor.Abstractions;

// UI.MudTail: the MudTail UI runtime every layout and module shares. It has no pages and no layouts.
//   css/mudtail.min.css  MudTail tokens and components (and the Tailwind preset), plus the host variables mapped to them;
//   js/module.js         runs without being listed: applies the saved theme, starts data-mud behaviours, and returns
//                        MudTail's Blazor interop, read with moduleHost.get('UI.MudTail') (MudTailRuntime).
// It loads on startup for everyone (the sign-in page uses it too) and stays active while layouts come and go, so
// swapping a layout keeps toasts, tables and the theme. Default settings, read when the module is seeded or uploaded.
[assembly: ClientModule(Name = "MudTail UI", LoadOnStartup = true, Hidden = true, RequiresAuth = false,
    Styles = new[] { "css/mudtail.min.css" })]
