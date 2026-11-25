using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;

namespace soft.square.net.EventsFixture.Publisher;

/// <summary>Test module A: places orders and publishes evtest.order-placed.v1 in the same transaction.</summary>
public sealed class PublisherModule : IRuntimeModule
{
    public ModuleManifest Manifest { get; } = new("evt-publisher", "Events publisher (test)", "1.0.0", "evt-publisher");

    public void ConfigureServices(IServiceCollection services, IConfiguration settings)
    {
        services.AddTenantDbContext<PublisherDb>();
        services.AddIntegrationEvents<PublisherDb>(o =>
        {
            o.SweepInterval = TimeSpan.FromSeconds(1);
            o.SweepDelay = TimeSpan.FromSeconds(1);
        });
    }
}

public sealed class Order
{
    public Guid Id { get; set; }
    public int Number { get; set; }
}

public sealed class PublisherDb(DbContextOptions<PublisherDb> options, ITenantContext tenant) : TenantDbContext(options, tenant)
{
    public const string SchemaName = "evt_publisher";
    protected override string Schema => SchemaName;
    protected override bool UsesIntegrationEvents => true;
    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) => modelBuilder.Entity<Order>().ToTable("orders");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        FixtureMigrations.IgnoreMissingSnapshot(optionsBuilder);
    }
}

[DbContext(typeof(PublisherDb))]
[Migration("20261125000000_Initial")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.EnsureSchema(PublisherDb.SchemaName);
        m.CreateTable("orders", schema: PublisherDb.SchemaName, columns: t => new
        {
            Id = t.Column<Guid>(type: "uuid", nullable: false),
            Number = t.Column<int>(type: "integer", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_orders", x => x.Id));
        FixtureMigrations.CreateIntegrationTables(m, PublisherDb.SchemaName);
    }
}

public sealed record OrderPlaced(Guid OrderId, int Number);

public sealed class OrderEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // ?fail=true: the order and its event are saved, then the transaction rolls back.
        app.MapPost("/orders", async (PublisherDb db, IIntegrationEvents events, int number, bool? fail) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var order = new Order { Id = Guid.NewGuid(), Number = number };
            db.Orders.Add(order);
            events.Publish("evtest.order-placed.v1", new OrderPlaced(order.Id, number), $"order:{order.Id}");
            await db.SaveChangesAsync();
            if (fail == true)
            {
                await transaction.RollbackAsync();
                return Results.Conflict();
            }
            await transaction.CommitAsync();
            return Results.Ok(order.Id);
        }).AllowAnonymous();
    }
}
