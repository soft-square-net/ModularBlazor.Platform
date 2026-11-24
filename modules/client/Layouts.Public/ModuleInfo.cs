using soft.square.net.ModularBlazor.Abstractions;

// Default settings of this module: read by Module Management when the file is uploaded.
// Not a startup module: the host loads it the first time a page asks for the "public" layout ([UseLayout("public")]
// or Layout = "public" in its module's settings), so admin-only tenants never download it, nor its stylesheet:
// css/site.min.css, MudTail's site pack (node-app copies it), which UI.MudTail's core stylesheet leaves out.
[assembly: ClientModule(Name = "Public layout", LoadOnStartup = false, Hidden = true, RequiresAuth = false,
    Dependencies = new[] { "UI.MudTail" }, Styles = new[] { "css/site.min.css" })]
