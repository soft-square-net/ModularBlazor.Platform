using soft.square.net.ModularBlazor.Abstractions;

// Default settings of this module: read by Module Management when the file is uploaded.
// The Tenancy API needs the platform permission platform.tenants.manage, which the platform-admin role grants.
[assembly: ClientModule(Name = "Tenants", Layout = "default", RequiredRole = "platform-admin",
    AvailableToRoles = new[] { "platform-admin" }, RoutePrefixes = new[] { "tenants" }, Styles = new[] { "tenants.css" },
    NavTitle = "Tenants", NavHref = "tenants", NavOrder = 30)]

// In Module.Admin's "Administration" group when that module is there; at the top of the secondary menu when it is not.
[assembly: MenuLink("tenants", "Tenants", "tenants", Zone = "secondary", Parent = "Module.Admin:admin", Order = 10)]
