using System.Text.Json;
using System.Text.Json.Serialization;

namespace soft.square.net.WebApi.Contracts.Tenancy;

/// <summary>Names shared by the host's seed reconciler and the modules that serve seeds.</summary>
public static class TenantSeedNames
{
    /// <summary>
    /// Configuration key naming this deployment (a gateway and its pools). A seed source lists only the seeds for this
    /// id, and the host reconciles nothing while it is not set.
    /// </summary>
    public const string DeploymentIdKey = "Platform:DeploymentId";

    /// <summary>The integration event a seed source publishes when a seed of this deployment changed (reconcile now).</summary>
    public const string SeedChangedEvent = "tenancy.seed-changed.v1";

    /// <summary>The grant source of the standing grants the reconciler writes for a seed's admins.</summary>
    public static string GrantSource(string seedId) => $"seed:{seedId}";
}

/// <summary>Where a tenant's database lives: a database server (a connection string name) and a database on it.</summary>
public sealed record TenantPlacement(string DatabaseServer, string DatabaseName);

/// <summary>
/// A tenant this deployment should have, as an outside system (the Organization service) describes it. The host
/// reconciles it into the tenant store: it creates the tenant, or updates it when <see cref="Version"/> grows.
/// </summary>
/// <param name="Id">The seed's own id; a tenant created from a seed stays bound to it.</param>
/// <param name="Version">Grows with every change of the seed. A seed version the tenant already has is a no-op.</param>
/// <param name="DeploymentId">The deployment that should host the tenant (<see cref="TenantSeedNames.DeploymentIdKey"/>).</param>
/// <param name="TenantId">The tenant's id: lowercase letters, digits and '-', at most 40 characters.</param>
/// <param name="DatabaseServer">The requested placement: a server the host knows (it may adjust or reject it).</param>
/// <param name="DatabaseName">The requested placement: the database on that server.</param>
/// <param name="Modules">The modules the tenant should use (informational for now).</param>
/// <param name="Admins">User ids (token subjects) that get a standing <c>access.grants.manage</c> grant in the tenant.</param>
/// <param name="Extra">Anything else the source wants to carry (branding, the unit-tree slice); the host ignores it.</param>
public sealed record TenantSeed(
    string Id,
    long Version,
    string DeploymentId,
    string TenantId,
    string Name,
    IReadOnlyList<string> Hosts,
    string DatabaseServer,
    string DatabaseName,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Admins,
    JsonElement Extra = default)
{
    public TenantPlacement RequestedPlacement => new(DatabaseServer, DatabaseName);
}

/// <summary>What the host reports back for a seed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SeedStatus>))]
public enum SeedStatus
{
    /// <summary>Known to the source, not yet seen by a host (the source's own initial state).</summary>
    Requested,
    /// <summary>The host accepted the seed and is creating or updating the tenant.</summary>
    Provisioning,
    /// <summary>The tenant exists as the seed asks, at the requested placement.</summary>
    Provisioned,
    /// <summary>The tenant exists as the seed asks, at another placement (reported as the actual placement).</summary>
    Adjusted,
    /// <summary>The host will not create the tenant (the reason says why); a new version of the seed is tried again.</summary>
    Rejected,
    /// <summary>Creating or updating the tenant failed (the reason says why); retried on the next cycle.</summary>
    Failed
}

/// <summary>
/// Serves the tenant seeds of a deployment and takes back their status. Implemented by a runtime module (the
/// Organization connector) and resolved from that module's own container on each reconciliation cycle, so the host
/// never holds it.
/// </summary>
public interface ITenantSeedSource
{
    /// <summary>The seeds for <paramref name="deploymentId"/> (all of them, whatever their state).</summary>
    Task<IReadOnlyList<TenantSeed>> ListAsync(string deploymentId, CancellationToken ct);

    /// <summary>The host's outcome for one version of a seed.</summary>
    Task ReportAsync(string seedId, long version, SeedStatus status, TenantPlacement? actualPlacement, string? reason, CancellationToken ct);
}

/// <summary>A placement policy's answer for a new tenant's seed.</summary>
public sealed record PlacementDecision
{
    private PlacementDecision(PlacementDecisionKind kind, TenantPlacement? placement, string? reason)
    {
        Kind = kind;
        Placement = placement;
        Reason = reason;
    }

    public PlacementDecisionKind Kind { get; }

    /// <summary>The placement to use instead (<see cref="PlacementDecisionKind.Adjust"/> only).</summary>
    public TenantPlacement? Placement { get; }

    /// <summary>Why the seed was rejected (<see cref="PlacementDecisionKind.Reject"/> only).</summary>
    public string? Reason { get; }

    public static PlacementDecision Accept { get; } = new(PlacementDecisionKind.Accept, null, null);

    public static PlacementDecision Adjust(TenantPlacement placement) =>
        new(PlacementDecisionKind.Adjust, placement ?? throw new ArgumentNullException(nameof(placement)), null);

    public static PlacementDecision Reject(string reason) =>
        new(PlacementDecisionKind.Reject, null, string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("A rejection needs a reason.", nameof(reason)) : reason);
}

public enum PlacementDecisionKind { Accept, Adjust, Reject }

/// <summary>
/// Decides where a seeded tenant's database goes. The seed's placement is a suggestion: a policy may accept it, move
/// it, or refuse the tenant. Asked only when a tenant is created (a database is never moved by a seed). The default,
/// <c>AcceptAsIs</c>, accepts every seed's placement. A host service: register another one in the host to replace it.
/// </summary>
public interface ITenantPlacementPolicy
{
    ValueTask<PlacementDecision> DecideAsync(TenantSeed seed, CancellationToken ct);
}
