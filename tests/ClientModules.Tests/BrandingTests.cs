using System.Text;
using soft.square.net.WebApi.Modules.Core.Branding;

namespace soft.square.net.ClientModules.Tests;

/// <summary>The branding core module's rules: what a layer may hold, and how the platform's and a tenant's combine.</summary>
public sealed class BrandingTests
{
    private const string Logo = "api/branding/assets/tenant-acme/logo-0123456789abcdef.png";

    [Fact]
    public void A_save_is_normalized()
    {
        var layer = BrandRules.Validate(new SaveBrandLayer(0, Name: "  Acme News ", ShortName: " ", Primary: "#AABBCC",
            LogoUrl: Logo, FaviconUrl: "https://cdn.example.com/acme.ico", DefaultMode: "Dark", AllowedModes: ["dark", "LIGHT", "dark"]));

        layer.Name.ShouldBe("Acme News");
        layer.ShortName.ShouldBeNull();
        layer.Primary.ShouldBe("#aabbcc");
        layer.LogoUrl.ShouldBe(Logo);
        layer.FaviconUrl.ShouldBe("https://cdn.example.com/acme.ico");
        layer.DefaultMode.ShouldBe("dark");
        layer.AllowedModes.ShouldBe(["dark", "light"]);
    }

    [Theory]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/api/branding/assets/tenant-acme/logo-0123456789abcdef.png")]
    [InlineData("api/branding/assets/tenant-acme/../../secrets.json")]
    [InlineData("api/branding/assets/tenant-acme/logo-0123456789abcdef.html")]
    public void Logos_are_uploads_or_https(string url) =>
        Should.Throw<BrandingException>(() => BrandRules.Validate(new SaveBrandLayer(0, LogoUrl: url)));

    [Theory]
    [InlineData("red")]
    [InlineData("#abc")]
    [InlineData("#aabbcc; background:url(x)")]
    public void Colours_are_hex(string color) =>
        Should.Throw<BrandingException>(() => BrandRules.Validate(new SaveBrandLayer(0, Primary: color)));

    [Fact]
    public void Fonts_radius_and_modes_are_checked()
    {
        Should.Throw<BrandingException>(() => BrandRules.Validate(new SaveBrandLayer(0, Font: "Cairo; } body { display:none")));
        Should.Throw<BrandingException>(() => BrandRules.Validate(new SaveBrandLayer(0, Radius: 40)));
        Should.Throw<BrandingException>(() => BrandRules.Validate(new SaveBrandLayer(0, DefaultMode: "sepia")));
        Should.Throw<BrandingException>(() => BrandRules.Validate(new SaveBrandLayer(0, DefaultMode: "dark", AllowedModes: ["light"])));
        BrandRules.Validate(new SaveBrandLayer(0, Font: "\"Noto Sans Arabic\", Cairo, sans-serif")).Font.ShouldNotBeNull();
    }

    [Fact]
    public void The_tenant_layer_wins_field_by_field_over_the_platform_and_the_default()
    {
        var platform = new BrandLayer { Name = "Platform", Primary = "#111111", Secondary = "#222222", Radius = 8 };
        var tenant = new BrandLayer { Name = "Acme", Primary = "#333333", AllowUserColors = false };

        var brand = BrandRules.Combine(platform, tenant);

        brand.Name.ShouldBe("Acme");
        brand.Primary.ShouldBe("#333333");
        brand.Secondary.ShouldBe("#222222");
        brand.Radius.ShouldBe(8);
        brand.AllowUserColors.ShouldBeFalse();
        brand.DefaultMode.ShouldBe("system");
        brand.AllowedModes.ShouldBe(BrandRules.Modes);

        BrandRules.Combine(new BrandLayer(), null).Name.ShouldBe(BrandRules.DefaultName);
    }

    [Fact]
    public void A_tenant_that_narrows_the_modes_moves_the_default_into_them()
    {
        var brand = BrandRules.Combine(new BrandLayer { DefaultMode = "dark" }, new BrandLayer { AllowedModes = ["light"] });

        brand.DefaultMode.ShouldBe("light");
    }

    [Fact]
    public void The_etag_follows_the_brand()
    {
        var platform = new BrandLayer { Name = "Platform" };

        BrandRules.Combine(platform, null).ETag.ShouldBe(BrandRules.Combine(platform with { Version = 3 }, null).ETag);
        BrandRules.Combine(platform, null).ETag.ShouldNotBe(BrandRules.Combine(platform with { Primary = "#123456" }, null).ETag);
    }

    [Fact]
    public void Images_must_be_what_their_name_says()
    {
        BrandRules.LooksLike(".png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]).ShouldBeTrue();
        BrandRules.LooksLike(".png", Encoding.UTF8.GetBytes("<html>")).ShouldBeFalse();
        BrandRules.LooksLike(".svg", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"/>")).ShouldBeTrue();
        BrandRules.LooksLike(".svg", Encoding.UTF8.GetBytes("<html><body>hi</body></html>")).ShouldBeFalse();
        BrandRules.LooksLike(".webp", Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ")).ShouldBeTrue();
    }
}
