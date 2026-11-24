using soft.square.net.ModularBlazor.Abstractions;

// Default settings of this module: read by Module Management when the file is uploaded.
// Not a startup module: the host loads it the first time a page asks for the "public" layout ([UseLayout("public")]
// or Layout = "public" in its module's settings), so admin-only tenants never download it.
[assembly: ClientModule(Name = "Public layout", LoadOnStartup = false, Hidden = true, RequiresAuth = false,
    Dependencies = new[] { "UI.MudTail" })]
