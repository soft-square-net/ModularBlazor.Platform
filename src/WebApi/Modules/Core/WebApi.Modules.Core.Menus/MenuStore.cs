using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace soft.square.net.WebApi.Modules.Core.Menus;

/// <summary>
/// The layers of menu changes (singleton), one small JSON file each, like the client-modules catalog: a stand-in for a
/// table every replica shares. <c>{Path}/platform.json</c> holds the platform's, <c>{Path}/tenants/{id}.json</c> each
/// tenant's. Saves happen under a cross-process lock file and replace the file atomically; reads use the last saved one.
/// </summary>
internal sealed partial class MenuStore
{
    public const string PlatformScope = "platform";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;
    private readonly Dictionary<string, (DateTime Written, long Length, MenuCustomization Value)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _cacheLock = new();

    public MenuStore(IOptions<MenusOptions> options, IHostEnvironment environment)
    {
        _root = Path.GetFullPath(options.Value.Path, environment.ContentRootPath);
        Directory.CreateDirectory(Path.Combine(_root, "tenants"));
    }

    /// <summary>A scope's layer: <see cref="PlatformScope"/> or a tenant id. Never saved yet: empty, version 0.</summary>
    public MenuCustomization Read(string scope)
    {
        var info = new FileInfo(FileOf(scope));
        if (!info.Exists) return new MenuCustomization();
        lock (_cacheLock)
            if (_cache.TryGetValue(scope, out var cached) && cached.Written == info.LastWriteTimeUtc && cached.Length == info.Length)
                return cached.Value;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var value = JsonSerializer.Deserialize<MenuCustomization>(File.ReadAllText(info.FullName), Json) ?? new MenuCustomization();
                lock (_cacheLock) _cache[scope] = (info.LastWriteTimeUtc, info.Length, value);
                return value;
            }
            catch (IOException) when (attempt < 20) // being replaced by another replica
            {
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>Replaces a scope's layer; 409 when it changed since <see cref="SaveMenuCustomization.Version"/> was read.</summary>
    public async Task<MenuCustomization> SaveAsync(string scope, SaveMenuCustomization save, string? user, CancellationToken ct)
    {
        var (overrides, items) = MenuRules.Validate(scope == PlatformScope ? MenuRules.PlatformLayer : MenuRules.TenantLayer, save);
        var path = FileOf(scope);
        using var _ = await LockAsync(path, ct);
        var current = File.Exists(path)
            ? JsonSerializer.Deserialize<MenuCustomization>(await File.ReadAllTextAsync(path, ct), Json) ?? new MenuCustomization()
            : new MenuCustomization();
        if (current.Version != save.Version)
            throw new MenuException($"The menus were changed by {current.UpdatedBy ?? "someone else"} meanwhile; load them again.", 409);

        var saved = new MenuCustomization
        {
            Version = current.Version + 1,
            Overrides = overrides,
            Items = items,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = user
        };
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(saved, Json), Encoding.UTF8, ct);
        File.Move(temp, path, overwrite: true);
        lock (_cacheLock) _cache.Remove(scope);
        return saved;
    }

    private string FileOf(string scope)
    {
        if (scope == PlatformScope) return Path.Combine(_root, "platform.json");
        if (!TenantId().IsMatch(scope)) throw new MenuException($"'{scope}' is not a tenant id.");
        return Path.Combine(_root, "tenants", scope.ToLowerInvariant() + ".json");
    }

    private static async Task<IDisposable> LockAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 400) // ~10 s
            {
                await Task.Delay(25, ct);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$")]
    private static partial Regex TenantId();
}
