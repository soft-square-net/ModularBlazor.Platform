using soft.square.net.ModularBlazor.Abstractions;

// Default settings of this module: read by Module Management when the file is uploaded.
// css/main.min.css (the layout shell) is built by node-app. MudTail itself (tokens, components, the JS runtime) is the
// UI.MudTail module's since 2.0.0, so swapping layouts keeps the runtime.
[assembly: ClientModule(Name = "Default layout", LoadOnStartup = true, Hidden = true, RequiresAuth = false,
    Dependencies = new[] { "UI.MudTail" }, Styles = new[] { "css/main.min.css" })]
