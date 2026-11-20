using Grpc.AspNetCore.Web;
using Grpc.Core;
using GrpcDemo.Protos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;

namespace soft.square.net.WebApi.Modules.GrpcDemo;

/// <summary>
/// Spike: gRPC inside a runtime module. Grpc.AspNetCore and Grpc.AspNetCore.Web are private dependencies; the service
/// is mapped in the module's private pipeline behind /api/grpcdemo/{**path}, so the gRPC method path
/// /grpcdemo.Greeter/SayHello is served at /api/grpcdemo/grpcdemo.Greeter/SayHello.
/// </summary>
public sealed class GrpcDemoModule : IRuntimeModule, IModulePipeline
{
    internal static readonly Version AssemblyVersion = typeof(GrpcDemoModule).Assembly.GetName().Version ?? new Version(1, 0, 0);
    public const string ReadPermission = "grpcdemo.read";

    public ModuleManifest Manifest { get; } = new("grpcdemo", "gRPC demo", $"{AssemblyVersion.Major}.{AssemblyVersion.Minor}.{AssemblyVersion.Build}", "grpcdemo")
    {
        Description = "Spike: gRPC and gRPC-Web served from a module's private pipeline.",
        Permissions = [new PermissionDefinition(ReadPermission, "Call the gRPC demo's WhoAmI.")],
#if NO_HOT_UNLOAD
        HotUnload = false
#endif
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings) => services.AddGrpc();

    public void ConfigurePipeline(IApplicationBuilder app)
    {
#if !HOST_APPLIES_ROUTE_OPTIONS
        CopyRouteConstraints(app.ApplicationServices);
#endif
        app.Use(ExposeGrpcHeadersToBrowsers);
        app.UseRouting();
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
#if METHOD_AUTHORIZATION
        // Per-method permissions: endpoint metadata on one gRPC method, evaluated by an AuthorizationMiddleware in the
        // private pipeline (the host's handlers and policy services).
        app.UseAuthorization();
        app.UseEndpoints(endpoints => endpoints.MapGrpcService<GreeterService>().Add(endpoint =>
        {
            if (endpoint.Metadata.OfType<Grpc.AspNetCore.Server.GrpcMethodMetadata>().FirstOrDefault()?.Method.Name == "Countdown")
                endpoint.Metadata.Add(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser().AddRequirements(new PermissionRequirement([ReadPermission])).Build());
        }));
#else
        app.UseEndpoints(endpoints => endpoints.MapGrpcService<GreeterService>());
#endif
    }

    /// <summary>
    /// Cross-origin browsers (the Blazor WASM client) can only read response headers listed in
    /// Access-Control-Expose-Headers. A gRPC error is a trailers-only response with grpc-status in the headers, so
    /// without this the client sees no status. The host's CORS policy exposes nothing; the module adds them itself.
    /// </summary>
    private static Task ExposeGrpcHeadersToBrowsers(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Headers.Origin.Count > 0)
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.Append("Access-Control-Expose-Headers", "Grpc-Status,Grpc-Message,Grpc-Status-Details-Bin");
                return Task.CompletedTask;
            });
        return next(context);
    }

    /// <summary>
    /// AddGrpc() registers the route constraint "grpcunimplemented" with Configure&lt;RouteOptions&gt;. The host gives the
    /// private pipeline a ready-made RouteOptions (Options.Create, registered after ConfigureServices), so module
    /// Configure actions never run on it and the matcher throws "The constraint reference 'grpcunimplemented' could not
    /// be resolved". Workaround: run the Configure actions on a scratch RouteOptions and copy the constraints it gained.
    /// </summary>
    private static void CopyRouteConstraints(IServiceProvider services)
    {
        var target = services.GetRequiredService<IOptions<RouteOptions>>().Value;
        var scratch = new RouteOptions();
        foreach (var configure in services.GetServices<IConfigureOptions<RouteOptions>>()) configure.Configure(scratch);
        foreach (var (name, type) in scratch.ConstraintMap) target.ConstraintMap.TryAdd(name, type);
    }

    // Authorization of every call is decided on the host's catch-all: a signed-in caller, tenant optional.
    public void ConfigureEndpoint(IEndpointConventionBuilder endpoint) => endpoint.TenantOptional();
}

public sealed class GreeterService(ITenantContext tenant, IAccessScopes access) : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context) =>
        Task.FromResult(new HelloReply
        {
            Message = $"Hello {request.Name}",
            ModuleVersion = GrpcDemoModule.AssemblyVersion.ToString(3)
        });

    public override async Task Countdown(CountdownRequest request, IServerStreamWriter<CountdownReply> responseStream, ServerCallContext context)
    {
        for (var i = request.Count; i > 0; i--)
        {
            await responseStream.WriteAsync(new CountdownReply { Value = i }, context.CancellationToken);
            if (request.IntervalMs > 0) await Task.Delay(request.IntervalMs, context.CancellationToken);
        }
    }

    public override async Task<WhoAmIReply> WhoAmI(WhoAmIRequest request, ServerCallContext context)
    {
        var http = context.GetHttpContext();
        // "enforce": the RequirePermission equivalent for one method, which the catch-all endpoint cannot express.
        var enforce = request.Permission == "enforce";
        var permission = enforce || request.Permission.Length == 0 ? GrpcDemoModule.ReadPermission : request.Permission;
        var allowed = await access.HasPermissionAsync(permission, context.CancellationToken);
        if (enforce && !allowed)
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"{GrpcDemoModule.ReadPermission} required"));
        return new WhoAmIReply
        {
            Authenticated = http.User.Identity?.IsAuthenticated ?? false,
            User = access.UserId ?? "",
            Tenant = tenant.TenantId ?? "",
            ResolvedBy = tenant.ResolvedBy ?? "",
            HasPermission = allowed,
            PathBase = http.Request.PathBase.Value ?? "",
            Protocol = http.Request.Protocol
        };
    }
}
