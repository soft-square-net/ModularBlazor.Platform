using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;

namespace soft.square.net.WebApi.Identity.Server.Auth;

/// <summary>
/// OpenIddict's application manager plus secret rotation with an overlap window: a confidential client may have a
/// second, "next" secret (hashed, in the application's properties) that is accepted alongside the current one. A
/// rotation creates it; the client's secret store gets it; once a token was issued with it the rotation can be
/// completed (it becomes the only secret). No moment where the client is locked out.
/// <para>
/// Also marks who manages an application (<see cref="ManagedBy"/>): <c>config</c> for the clients the seeder
/// registers from configuration (edited there, never in the admin UI), <c>ui</c> for the ones created in the admin UI
/// (the seeder never touches them).
/// </para>
/// </summary>
public sealed class RotatingApplicationManager(
    IOpenIddictApplicationCache<OpenIddictEntityFrameworkCoreApplication> cache,
    ILogger<OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication> store)
    : OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication>(cache, logger, options, store)
{
    public const string ManagedBy = "managed_by";
    public const string ManagedByConfig = "config";
    public const string ManagedByUi = "ui";
    public const string NextSecret = "next_secret";
    public const string NextSecretCreated = "next_secret_created";
    public const string NextSecretUsed = "next_secret_used";
    public const string SecretRotated = "secret_rotated";

    /// <summary>The current secret, or (during a rotation) the next one; the first use of the next one is recorded.</summary>
    public override async ValueTask<bool> ValidateClientSecretAsync(OpenIddictEntityFrameworkCoreApplication application, string secret,
        CancellationToken cancellationToken = default)
    {
        if (await base.ValidateClientSecretAsync(application, secret, cancellationToken)) return true;

        var properties = await Store.GetPropertiesAsync(application, cancellationToken);
        if (Text(properties, NextSecret) is not { } next || !await ValidateClientSecretAsync(secret, next, cancellationToken)) return false;
        if (!properties.ContainsKey(NextSecretUsed))
            await SetPropertiesAsync(application, properties.SetItem(NextSecretUsed, Now()), cancellationToken);
        return true;
    }

    /// <summary>Whether <paramref name="secret"/> is the application's current secret (not the next one).</summary>
    public async ValueTask<bool> IsCurrentSecretAsync(OpenIddictEntityFrameworkCoreApplication application, string secret, CancellationToken ct) =>
        await Store.GetClientSecretAsync(application, ct) is { Length: > 0 } hash && await ValidateClientSecretAsync(secret, hash, ct);

    /// <summary>Whether <paramref name="secret"/> is the application's pending next secret.</summary>
    public async ValueTask<bool> IsNextSecretAsync(OpenIddictEntityFrameworkCoreApplication application, string secret, CancellationToken ct) =>
        Text(await Store.GetPropertiesAsync(application, ct), NextSecret) is { } next && await ValidateClientSecretAsync(secret, next, ct);

    public ValueTask<string?> GetSecretHashAsync(OpenIddictEntityFrameworkCoreApplication application, CancellationToken ct) =>
        Store.GetClientSecretAsync(application, ct);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetStoredPropertiesAsync(OpenIddictEntityFrameworkCoreApplication application, CancellationToken ct) =>
        Store.GetPropertiesAsync(application, ct);

    public async ValueTask SetPropertiesAsync(OpenIddictEntityFrameworkCoreApplication application, ImmutableDictionary<string, JsonElement> properties,
        CancellationToken ct)
    {
        await Store.SetPropertiesAsync(application, properties, ct);
        await base.UpdateAsync(application, ct);
    }

    /// <summary>Starts a rotation: a new secret, accepted next to the current one. Returns it (shown once).</summary>
    public async ValueTask<string> StartRotationAsync(OpenIddictEntityFrameworkCoreApplication application, CancellationToken ct)
    {
        var secret = NewSecret();
        var properties = (await Store.GetPropertiesAsync(application, ct))
            .Remove(NextSecretUsed)
            .SetItem(NextSecret, JsonSerializer.SerializeToElement(await ObfuscateClientSecretAsync(secret, ct)))
            .SetItem(NextSecretCreated, Now());
        await SetPropertiesAsync(application, properties, ct);
        return secret;
    }

    /// <summary>Ends a rotation: the next secret becomes the only one (the old one stops working now).</summary>
    public async ValueTask CompleteRotationAsync(OpenIddictEntityFrameworkCoreApplication application, CancellationToken ct)
    {
        var properties = await Store.GetPropertiesAsync(application, ct);
        if (Text(properties, NextSecret) is not { } next) return;
        await Store.SetClientSecretAsync(application, next, ct); // already hashed
        await SetPropertiesAsync(application, WithoutRotation(properties).SetItem(SecretRotated, Now()), ct);
    }

    /// <summary>Drops a pending next secret (the current one stays).</summary>
    public async ValueTask CancelRotationAsync(OpenIddictEntityFrameworkCoreApplication application, CancellationToken ct) =>
        await SetPropertiesAsync(application, WithoutRotation(await Store.GetPropertiesAsync(application, ct)), ct);

    /// <summary>Replaces the secret at once (no overlap), for clients managed in the admin UI. Returns it (shown once).</summary>
    public async ValueTask<string> ReplaceSecretAsync(OpenIddictEntityFrameworkCoreApplication application, CancellationToken ct)
    {
        var secret = NewSecret();
        await Store.SetPropertiesAsync(application, WithoutRotation(await Store.GetPropertiesAsync(application, ct)).SetItem(SecretRotated, Now()), ct);
        await base.UpdateAsync(application, secret, ct); // hashes it
        return secret;
    }

    /// <summary>Keeps an already hashed secret as the current one (the seeder, after a rotation).</summary>
    public async ValueTask SetSecretHashAsync(OpenIddictEntityFrameworkCoreApplication application, string hash, CancellationToken ct)
    {
        await Store.SetClientSecretAsync(application, hash, ct);
        await base.UpdateAsync(application, ct);
    }

    public static ImmutableDictionary<string, JsonElement> WithoutRotation(ImmutableDictionary<string, JsonElement> properties) =>
        properties.Remove(NextSecret).Remove(NextSecretCreated).Remove(NextSecretUsed);

    public static string? Text(IReadOnlyDictionary<string, JsonElement> properties, string name) =>
        properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static DateTimeOffset? Time(IReadOnlyDictionary<string, JsonElement> properties, string name) =>
        Text(properties, name) is { } text && DateTimeOffset.TryParse(text, out var at) ? at : null;

    /// <summary>256 random bits, base64url: what a secret store holds.</summary>
    public static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JsonElement Now() => JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToString("O"));
}
