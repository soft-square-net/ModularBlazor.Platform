using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts.Lookups;
using soft.square.net.WebApi.Lookups;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Tests.Lookups;

public enum StockKind { Hardware, Tools, Paint }

public sealed class StockItem
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameAr { get; set; }
    public int Quantity { get; set; }
    public StockKind Kind { get; set; }
    public DateTime? Received { get; set; }
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
}

public sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
{
    public DbSet<StockItem> Items => Set<StockItem>();
    public DbSet<Category> Categories => Set<Category>();
}

public sealed class InventoryLookups : ILookupModule
{
    public void Register(ILookupBuilder l)
    {
        l.For<StockItem>("stock-items")
            .Key(i => i.Id)
            .Text(i => i.Sku + " - " + i.Name)
            .Text("ar", i => i.Sku + " - " + (i.NameAr ?? i.Name))
            .SearchOn(nameof(StockItem.Sku), nameof(StockItem.Name))
            .Where(i => i.Quantity >= 0)
            .OrderBy(i => i.Sku)
            .FilterOn(nameof(StockItem.Name))
            .FilterOn(nameof(StockItem.Kind))
            .FilterOn(nameof(StockItem.Quantity))
            .FilterOn(nameof(StockItem.Received))
            .Columns(nameof(StockItem.Sku), nameof(StockItem.Quantity));

        l.For<Category>("categories").Key(c => c.Id).TextField(nameof(Category.Name)).Hierarchy(nameof(Category.ParentId));
    }
}

public sealed class LookupDefinitionTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _services = null!;
    private LookupSet _set = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        _services = new ServiceCollection()
            .AddDbContext<TestDb>(o => o.UseSqlite(_connection))
            .AddScoped<DbContext>(sp => sp.GetRequiredService<TestDb>())
            .BuildServiceProvider();
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDb>();
        await db.Database.EnsureCreatedAsync();
        db.Items.AddRange(
            new StockItem { Id = 1, Sku = "SKU-1", Name = "Widget", NameAr = "قطعة", Quantity = 12, Kind = StockKind.Hardware, Received = new DateTime(2026, 1, 5) },
            new StockItem { Id = 2, Sku = "SKU-2", Name = "Gear", Quantity = 4, Kind = StockKind.Tools, Received = new DateTime(2026, 3, 1) },
            new StockItem { Id = 3, Sku = "BOLT-7", Name = "Bolt", Quantity = 0, Kind = StockKind.Hardware },
            new StockItem { Id = 4, Sku = "HIDDEN", Name = "Hidden widget", Quantity = -1, Kind = StockKind.Paint });
        db.Categories.AddRange(
            new Category { Id = 1, Name = "Electronics" },
            new Category { Id = 2, Name = "Phones", ParentId = 1 },
            new Category { Id = 3, Name = "Android", ParentId = 2 },
            new Category { Id = 4, Name = "Garden" });
        await db.SaveChangesAsync();
        _set = LookupSet.Discover(typeof(InventoryLookups).Assembly, "inventory", "inventory", new HashSet<string>(), typeof(InventoryLookups))!;
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<LookupPage> Search(string name, LookupQuery query)
    {
        using var scope = _services.CreateScope();
        return await _set.Get(name).SearchAsync(scope.ServiceProvider, query, CancellationToken.None);
    }

    private async Task<IReadOnlyList<LookupItem>> Resolve(string name, string[] keys, IReadOnlyList<LookupFilter>? filters = null)
    {
        using var scope = _services.CreateScope();
        return await _set.Get(name).ResolveAsync(scope.ServiceProvider, keys, filters, CancellationToken.None);
    }

    [Fact]
    public async Task Empty_query_lists_the_first_page_in_order_with_the_fixed_filter()
    {
        var page = await Search("stock-items", new LookupQuery { Take = 2 });

        page.Items.Select(i => i.Text).ShouldBe(["BOLT-7 - Bolt", "SKU-1 - Widget"]);
        page.HasMore.ShouldBeTrue();
        page.Items[1].Key.ShouldBe("1");
        page.Items[1].Extra!["Quantity"].ShouldBe("12");
    }

    [Fact]
    public async Task Search_is_case_insensitive_on_the_search_fields_and_narrowable()
    {
        (await Search("stock-items", new LookupQuery { Q = "widg" })).Items.Select(i => i.Key).ShouldBe(["1"]); // "Hidden widget" fails Where
        (await Search("stock-items", new LookupQuery { Q = "sku" })).Items.Count.ShouldBe(2);
        (await Search("stock-items", new LookupQuery { Q = "sku", Fields = ["Name"] })).Items.ShouldBeEmpty();
        await Should.ThrowAsync<ArgumentException>(() => Search("stock-items", new LookupQuery { Q = "x", Fields = ["Quantity"] }));
    }

    [Fact]
    public async Task Filters_combine_with_and_and_report_the_total()
    {
        var page = await Search("stock-items", new LookupQuery
        {
            IncludeTotal = true,
            Filters =
            [
                new LookupFilter("Kind", "in", Values: ["Hardware", "Tools"]),
                new LookupFilter("Quantity", "between", Values: ["1", "20"]),
                new LookupFilter("Name", "contains", "EA")
            ]
        });

        page.Items.Select(i => i.Key).ShouldBe(["2"]);
        page.Total.ShouldBe(1);
        (await Search("stock-items", new LookupQuery { Filters = [new LookupFilter("Received", "isEmpty")] })).Items.Select(i => i.Key).ShouldBe(["3"]);
        (await Search("stock-items", new LookupQuery { Filters = [new LookupFilter("Received", "gte", "2026-02-01")] })).Items.Select(i => i.Key).ShouldBe(["2"]);
    }

    [Theory]
    [InlineData("Sku", "eq", "x")]          // not a filter field
    [InlineData("Kind", "contains", "Hard")] // not an operator of an enum
    [InlineData("Quantity", "gt", "many")]   // not a number
    public async Task Invalid_filters_are_rejected(string field, string op, string value) =>
        await Should.ThrowAsync<ArgumentException>(() => Search("stock-items", new LookupQuery { Filters = [new LookupFilter(field, op, value)] }));

    [Fact]
    public async Task Resolve_returns_existing_keys_that_pass_the_fixed_and_given_filters()
    {
        (await Resolve("stock-items", ["1", "2", "4", "99", "not-a-number"])).Select(i => i.Key).Order().ShouldBe(["1", "2"]);
        (await Resolve("stock-items", ["1", "2"], [new LookupFilter("Kind", "eq", "Tools")])).Select(i => i.Key).ShouldBe(["2"]);
    }

    [Fact]
    public async Task Hierarchy_lists_roots_then_children_with_has_children_and_parent_keys()
    {
        var roots = await Search("categories", new LookupQuery());
        roots.Items.Select(i => (i.Text, i.HasChildren)).ShouldBe([("Electronics", (bool?)true), ("Garden", false)]);

        var phones = await Search("categories", new LookupQuery { Parent = "1" });
        phones.Items.Single().ShouldSatisfyAllConditions(i => i.Text.ShouldBe("Phones"), i => i.ParentKey.ShouldBe("1"), i => i.HasChildren.ShouldBe(true));

        var path = await Resolve("categories", ["1", "2", "3"]);
        path.Single(i => i.Key == "3").ParentKey.ShouldBe("2");
        path.Single(i => i.Key == "3").HasChildren.ShouldBe(false);
    }

    [Fact]
    public async Task The_culture_picks_its_text()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ar-EG");
        try
        {
            (await Resolve("stock-items", ["1", "2"])).Select(i => i.Text).Order().ShouldBe(["SKU-1 - قطعة", "SKU-2 - Gear"]);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void The_catalog_describes_the_lookups()
    {
        var items = _set.Get("stock-items").Describe("inventory", "inventory");
        items.Key.ShouldBe("inventory.stock-items");
        items.SearchFields.ShouldBe(["Sku", "Name"]);
        items.FilterFields.Single(f => f.Name == "Kind").Ops.ShouldBe(["eq", "ne", "in"]);
        items.FilterFields.Single(f => f.Name == "Received").Ops.ShouldContain("isEmpty");
        _set.Get("categories").Describe("inventory", "inventory").ParentKind.ShouldBe("tree");
    }

    [Fact]
    public void Invalid_definitions_fail_with_the_reason()
    {
        var error = Should.Throw<ModuleException>(() => LookupSet.Discover(typeof(BrokenLookups).Assembly, "broken", "broken", null, typeof(BrokenLookups)));
        error.Message.ShouldContain("has no public property 'Nmae'");
    }

    [Fact]
    public void An_undeclared_permission_fails_activation()
    {
        var error = Should.Throw<ModuleException>(() => LookupSet.Discover(typeof(GuardedLookups).Assembly, "guarded", "guarded", new HashSet<string>(), typeof(GuardedLookups)));
        error.Message.ShouldContain("'inventory.read'");
    }
}

public sealed class BrokenLookups : ILookupModule
{
    public void Register(ILookupBuilder l) => l.For<StockItem>("broken").Key(i => i.Id).TextField("Nmae");
}

public sealed class GuardedLookups : ILookupModule
{
    public void Register(ILookupBuilder l) => l.For<StockItem>("guarded").Key(i => i.Id).TextField("Name").Permission("inventory.read");
}
