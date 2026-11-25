using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace soft.square.net.WebApi.Contracts;

/// <summary>
/// Entry point of a module the host loads at runtime from an uploaded .dll, .zip or .nupkg.
/// A package has exactly one public, non-abstract implementation with a parameterless constructor.
/// Endpoints come from the package's <c>ICarterModule</c> types; they are mapped under the manifest's route prefix.
/// </summary>
public interface IRuntimeModule
{
    /// <summary>Read once at install time and registered in the module store.</summary>
    ModuleManifest Manifest { get; }

    /// <summary>
    /// Registers the module's own services. They live in a module-level container that falls back to the
    /// host for everything else (logging, options, tenant context, ...).
    /// </summary>
    /// <param name="settings">The module's settings: the manifest defaults, overridden by the admin.</param>
    /// <remarks>
    /// <c>IHostedService</c>s registered here (<c>AddHostedService&lt;T&gt;()</c>, a <c>BackgroundService</c>) are started
    /// when the module is loaded, on Api and Worker pools alike, and stopped (awaited, with a timeout) before the
    /// module's services are disposed: on unload, on a swap to a new version or new settings, and on host shutdown.
    /// </remarks>
    void ConfigureServices(IServiceCollection services, IConfiguration settings);
}

/// <param name="Id">Stable, lowercase identifier (a-z, 0-9, '.', '-').</param>
/// <param name="RoutePrefix">Segment the module's endpoints are mapped under, e.g. "greeting" → /api/greeting/...</param>
public sealed record ModuleManifest(string Id, string Name, string Version, string RoutePrefix)
{
    public string? Description { get; init; }

    /// <summary>Default settings copied into the module store on install; the admin can override them.</summary>
    public IReadOnlyDictionary<string, string?> DefaultSettings { get; init; } = new Dictionary<string, string?>();

    /// <summary>
    /// Permissions the module's endpoints require (<c>RequirePermission</c>), listed in the permission catalog. Activation
    /// fails when an endpoint requires a permission that is neither declared here nor a platform permission.
    /// </summary>
    public IReadOnlyList<Security.PermissionDefinition> Permissions { get; init; } = [];

    /// <summary>
    /// The live topic kinds the module publishes (<c>{Id}:{kind}:{key}</c>) and the permission a subscriber needs for
    /// each. Data the host reads at install: the live hub authorizes subscriptions from it, never through module code.
    /// </summary>
    public IReadOnlyList<Live.LiveTopic> LiveTopics { get; init; } = [];

    /// <summary>
    /// True (default): the module can be unloaded, uninstalled and swapped to a new version or new settings while
    /// the host runs. Set it to false when the module, or a library it hosts, keeps process-wide state that pins its
    /// assemblies (static caches, global registrations). The runtime then refuses unload and uninstall with 409
    /// (unless they are deferred to the next restart), and an upgrade or settings change keeps the running copy and
    /// reports <c>restartRequired</c>. Activate and deactivate still work.
    /// </summary>
    public bool HotUnload { get; init; } = true;

    /// <summary>
    /// Integration event type patterns the module handles (<see cref="Messaging.IntegrationTypes"/>), e.g.
    /// <c>["org.unit-*.v1", "shifts.checked-in.v1"]</c>. Data read at install, without loading the module: the host
    /// binds the module's durable queue (<c>integration.{id}.{pool}</c>) to them, so events wait there while the module
    /// is unloaded. Register a handler for each with <c>services.AddIntegrationHandler&lt;T&gt;(pattern)</c>.
    /// </summary>
    public IReadOnlyList<string> Subscriptions { get; init; } = [];
}
