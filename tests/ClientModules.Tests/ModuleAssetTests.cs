using System.IO.Compression;
using System.Reflection;
using System.Security.Claims;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Modules.Core.ClientModules;

namespace soft.square.net.ClientModules.Tests;

/// <summary>
/// A module's static files are taken out of its package on install and served from per-version URLs that only a
/// manifest hands out.
/// </summary>
public sealed class ModuleAssetTests : IDisposable
{
    private static readonly string FixturePath = typeof(ModuleAssetTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "FixtureModule").Value!;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "client-module-assets-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ClientModuleCatalog Catalog() => new(
        Options.Create(new ClientModulesOptions { Path = _root }),
        new Environment(_root),
        NullLogger<ClientModuleCatalog>.Instance);

    private static async Task Install(ClientModuleCatalog catalog, string fileName, byte[] content)
    {
        var upload = await catalog.UploadAsync(fileName, new MemoryStream(content), "test", CancellationToken.None);
        await catalog.InstallAsync(upload.Token, new InstallClientModule(new ClientModuleSettings
        {
            RequiresAuth = false, RoutePrefixes = ["fixture"], Styles = ["css/fixture.css"]
        }, Replace: true), "test", CancellationToken.None);
    }

    private static ClientModuleDescriptor Described(ClientModuleCatalog catalog) =>
        catalog.Manifest(new ClaimsPrincipal(), null, new HashSet<string>()).Modules.Single(m => m.Id == "Module.Fixture");

    /// <summary>"files/{id}/{version}/{token}/" of an asset base.</summary>
    private static (string Id, string Version, string Token) Parts(string assetBase)
    {
        var parts = assetBase.Split('/');
        return (Uri.UnescapeDataString(parts[3]), Uri.UnescapeDataString(parts[4]), parts[5]);
    }

    [Theory]
    [InlineData("wwwroot\\css\\a.css", "css/a.css")]
    [InlineData("/js/module.js", "js/module.js")]
    [InlineData("_content/My.Module/js/x.js", "js/x.js")]
    [InlineData("../secrets.json", null)]
    [InlineData("js/../../x.js", null)]
    [InlineData("js//x.js", null)]
    [InlineData("C:/x.js", null)]
    public void Paths_are_plain_relative_paths(string path, string? expected) =>
        ClientModuleAssets.Normalize(path).ShouldBe(expected);

    [Fact]
    public void Embedded_files_are_extracted_without_loading_the_assembly()
    {
        var assets = ClientModuleAssets.Extract(ClientModuleKind.Dll, [("fixture.dll", File.ReadAllBytes(FixturePath))]);

        assets.Select(a => a.Path).ShouldBe(["css/fixture.css", "js/chunks/hello-a1b2c3.js", "js/module.js"]);
    }

    [Fact]
    public void A_nupkg_s_static_web_assets_win_over_embedded_ones()
    {
        using var nupkg = new MemoryStream();
        using (var zip = new ZipArchive(nupkg, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var dll = zip.CreateEntry("lib/net10.0/fixture.dll").Open()) dll.Write(File.ReadAllBytes(FixturePath));
            using (var css = new StreamWriter(zip.CreateEntry("staticwebassets/css/fixture.css").Open())) css.Write(".from-package {}");
            using (var bad = new StreamWriter(zip.CreateEntry("staticwebassets/../evil.js").Open())) bad.Write("x");
        }

        var assets = ClientModuleAssets.Extract(ClientModuleKind.NuPkg, [("fixture.1.0.0.nupkg", nupkg.ToArray())]);

        assets.Select(a => a.Path).ShouldBe(["css/fixture.css", "js/chunks/hello-a1b2c3.js", "js/module.js"]);
        System.Text.Encoding.UTF8.GetString(assets[0].Content).ShouldBe(".from-package {}");
    }

    [Fact]
    public async Task An_installed_module_s_files_are_served_from_its_asset_base()
    {
        var catalog = Catalog();
        await Install(catalog, "fixture.dll", File.ReadAllBytes(FixturePath));

        var module = Described(catalog);
        module.AssetBase.ShouldNotBeNull();
        module.AssetBase.ShouldStartWith("api/client-modules/files/Module.Fixture/");
        module.AssetHashes!.Keys.ShouldBe(["css/fixture.css", "js/module.js"], ignoreOrder: true); // what the client activates itself
        module.ProvidesLayouts.ShouldBe(["fixture"]); // a page asking for "fixture" loads this module first

        var (id, version, token) = Parts(module.AssetBase);
        var chunk = catalog.ResolveAsset(id, version, token, "js/chunks/hello-a1b2c3.js");
        chunk.ShouldNotBeNull();
        File.ReadAllText(chunk.Value.Path).ShouldContain("hello");
        chunk.Value.Public.ShouldBeTrue();

        catalog.ResolveAsset(id, version, token.Replace(token[0], token[0] == 'a' ? 'b' : 'a'), "js/module.js").ShouldBeNull();
        catalog.ResolveAsset(id, version, token, "../fixture.dll").ShouldBeNull();
        catalog.ResolveAsset(id, version, token, "js/missing.js").ShouldBeNull();
        catalog.ResolveAsset(id, "9.9.9", token, "js/module.js").ShouldBeNull();
    }

    [Fact]
    public async Task Versions_installed_before_extraction_get_their_files_on_the_next_start()
    {
        var catalog = Catalog();
        await Install(catalog, "fixture.dll", File.ReadAllBytes(FixturePath));

        // Forget the extraction, as a catalog written by an older server.
        var records = Path.Combine(_root, "client-modules.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(records))!;
        foreach (var version in json[0]!["versions"]!.AsArray()) { version!.AsObject().Remove("assets"); version.AsObject().Remove("assetsHash"); }
        File.WriteAllText(records, json.ToJsonString());
        Directory.Delete(Directory.GetDirectories(Path.Combine(_root, "files", "Module.Fixture")).Single() + "/assets", recursive: true);
        Catalog().List().Single().Active!.Assets.ShouldBeNull();
        Described(Catalog()).AssetBase.ShouldBeNull(); // the client keeps its in-memory path meanwhile

        var fresh = Catalog();
        (await fresh.ExtractMissingAssetsAsync(CancellationToken.None)).ShouldBe(1);
        (await fresh.ExtractMissingAssetsAsync(CancellationToken.None)).ShouldBe(0);

        var module = Described(fresh);
        var (id, version2, token) = Parts(module.AssetBase!);
        fresh.ResolveAsset(id, version2, token, "css/fixture.css").ShouldNotBeNull();
    }

    [Fact]
    public async Task Seeding_adds_the_dependencies_a_shipped_module_needs_and_keeps_the_admin_s_other_settings()
    {
        var seed = Path.Combine(_root, "seed");
        var folder = Path.Combine(seed, "Module.Fixture");
        Directory.CreateDirectory(folder);
        File.Copy(FixturePath, Path.Combine(folder, Path.GetFileName(FixturePath)));
        ClientModuleCatalog Seeded() => new(Options.Create(new ClientModulesOptions { Path = Path.Combine(_root, "catalog"), SeedPath = seed }),
            new Environment(_root), NullLogger<ClientModuleCatalog>.Instance);

        await Seeded().SeedAsync(CancellationToken.None);
        await Seeded().UpdateSettingsAsync("Module.Fixture",
            Seeded().Get("Module.Fixture").Settings with { Name = "Renamed by the admin" }, "admin", CancellationToken.None);

        // The same files, now declaring a dependency (a release whose module needs another one).
        File.WriteAllText(Path.Combine(folder, ClientModuleInspector.SettingsFile), """{ "dependencies": ["UI.MudTail"] }""");
        await Seeded().SeedAsync(CancellationToken.None);

        var settings = Seeded().Get("Module.Fixture").Settings;
        settings.Dependencies.ShouldBe(["UI.MudTail"]);
        settings.Name.ShouldBe("Renamed by the admin");
    }

    private sealed class Environment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
