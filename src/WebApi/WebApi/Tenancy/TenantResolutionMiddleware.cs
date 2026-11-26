using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Tenancy;

/// <summary>
/// Resolves the tenant of every request, on every replica:
/// <list type="number">
/// <item>the <c>X-Tenant</c> header, then the host name (X-Forwarded-Host from the gateway, else Host); if both name a
/// tenant they must agree (400);</item>
/// <item>with a signed-in user, the token's <c>tenant</c> claim is the authority: the request's tenant must equal it (403),
/// or it is the tenant when the request names none;</item>
/// <item>an unknown tenant is 404 (header) or 403 (token), a suspended one 403;</item>
/// <item>except on an endpoint marked <see cref="TenantOptionalMetadata"/>: there a tenant this deployment does not know,
/// named by the header or the token, is a <em>foreign</em> tenant. The request runs without a tenant and
/// <see cref="ITenantContext.ForeignTenantId"/> names it, so the endpoint knows the caller came from another deployment's
/// tenant; grants and platform roles give such a caller nothing here (<c>AccessScopes</c>). A tenant this deployment
/// knows is never foreign: suspended is still 403, and a header that disagrees with the token is still 403.</item>
/// </list>
/// Runtime-module endpoints need a tenant (400 without one) unless they are marked <see cref="TenantOptionalMetadata"/>,
/// and wait until the module is initialized for that tenant (migrations; 503 if that failed). Host endpoints never
/// need a tenant.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next, TenantDirectory directory, ModuleTenantInitializer initializer,
    ILogger<TenantResolutionMiddleware> log)
{
    private static readonly TimeSpan InitializationWait = TimeSpan.FromSeconds(30);

    public async Task InvokeAsync(HttpContext context)
    {
        TenantInfo? tenant = null;
        string? resolvedBy = null;
        string? foreign = null, foreignBy = null;

        var endpoint = context.GetEndpoint();
        var tenantOptional = endpoint?.Metadata.GetMetadata<TenantOptionalMetadata>() is not null;

        if (context.Request.Headers.TryGetValue(TenancyNames.Header, out var header) && header.ToString() is { Length: > 0 } id)
        {
            tenant = directory.Find(id);
            if (tenant is not null) resolvedBy = "header";
            else if (tenantOptional) (foreign, foreignBy) = (id, "header");
            else { await Problem(context, 404, $"Unknown tenant '{id}'."); return; }
        }

        if (directory.FindByHost(RequestHost(context)) is { } byHost)
        {
            if (foreign is not null || (tenant is not null && !Same(tenant, byHost)))
            {
                await Problem(context, 400, $"The {TenancyNames.Header} header and the host name name different tenants."); return;
            }
            if (tenant is null) (tenant, resolvedBy) = (byHost, "host");
        }

        if (context.User.Identity?.IsAuthenticated == true && context.User.FindFirst(TenancyNames.Claim)?.Value is { Length: > 0 } claimed)
        {
            if (foreign is not null)
            {
                // The header named a tenant this deployment does not know: the token must be for that same tenant.
                if (!string.Equals(foreign, claimed, StringComparison.OrdinalIgnoreCase))
                {
                    await Problem(context, 403, "The request's tenant is not the tenant the token was issued for."); return;
                }
                foreignBy = "claim";
            }
            else if (tenant is null)
            {
                tenant = directory.Find(claimed);
                if (tenant is not null) resolvedBy = "claim";
                else if (tenantOptional) (foreign, foreignBy) = (claimed, "claim");
                else { await Problem(context, 403, $"The token's tenant '{claimed}' does not exist."); return; }
            }
            else if (!string.Equals(tenant.Id, claimed, StringComparison.OrdinalIgnoreCase))
            {
                await Problem(context, 403, "The request's tenant is not the tenant the token was issued for."); return;
            }
        }

        if (tenant is not null)
        {
            if (!tenant.IsActive) { await Problem(context, 403, $"Tenant '{tenant.Id}' is {tenant.Status.ToString().ToLowerInvariant()}."); return; }
            context.RequestServices.GetRequiredService<ITenantScopeInitializer>().Use(tenant, resolvedBy!);
        }
        else if (foreign is not null)
        {
            context.RequestServices.GetRequiredService<TenantContext>().UseForeign(foreign, foreignBy!);
            log.LogDebug("Foreign tenant {Tenant} (from the {Source}) on {Path}: the request runs without a tenant",
                foreign, foreignBy, context.Request.Path);
        }

        if (endpoint?.Metadata.GetMetadata<RuntimeModuleMetadata>() is { } module)
        {
            if (tenant is null)
            {
                if (!tenantOptional)
                {
                    await Problem(context, 400, $"This endpoint needs a tenant: send the {TenancyNames.Header} header or use the tenant's host name.");
                    return;
                }
            }
            else
            {
                try { await initializer.ReadyAsync(module.ModuleId, tenant).WaitAsync(InitializationWait, context.RequestAborted); }
                catch (Exception e) when (e is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
                {
                    log.LogWarning(e, "Module {Module} is not ready for tenant {Tenant}", module.ModuleId, tenant.Id);
                    await Problem(context, 503, $"Module '{module.ModuleId}' is not ready for tenant '{tenant.Id}' yet.");
                    return;
                }
            }
        }

        await next(context);
    }

    private static string RequestHost(HttpContext context)
    {
        // The gateway sets X-Forwarded-Host (overwriting any client value). Production: ForwardedHeaders with known proxies.
        var forwarded = context.Request.Headers["X-Forwarded-Host"].ToString();
        var host = forwarded.Length > 0 ? forwarded.Split(',')[0].Trim() : context.Request.Host.Value ?? "";
        var colon = host.LastIndexOf(':');
        return colon > 0 && !host.EndsWith(']') ? host[..colon] : host;
    }

    private static bool Same(TenantInfo a, TenantInfo b) => string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);

    private static Task Problem(HttpContext context, int status, string detail) =>
        Results.Problem(detail, statusCode: status, title: "Tenant").ExecuteAsync(context);
}
