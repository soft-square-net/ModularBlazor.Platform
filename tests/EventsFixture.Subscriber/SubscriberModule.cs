using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;

namespace soft.square.net.EventsFixture.Subscriber;

/// <summary>Test module B: records every evtest.order-*.v1 event it handles (into its own copy of the event record).</summary>
public sealed class SubscriberModule : IRuntimeModule
{
    public ModuleManifest Manifest { get; } = new("evt-subscriber", "Events subscriber (test)", "1.0.0", "evt-subscriber")
    {
        Subscriptions = ["evtest.order-*.v1"]
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings)
    {
        services.AddTenantDbContext<SubscriberDb>();
        services.AddIntegrationEvents<SubscriberDb>();
        services.AddIntegrationHandler<OrderHandler>("evtest.order-*.v1");
    }
}

public sealed class Received
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
    public int Number { get; set; }
}

public sealed class SubscriberDb(DbContextOptions<SubscriberDb> options, ITenantContext tenant) : TenantDbContext(options, tenant)
{
    public const string SchemaName = "evt_subscriber";
    protected override string Schema => SchemaName;
    protected override bool UsesIntegrationEvents => true;
    public DbSet<Received> Received => Set<Received>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) => modelBuilder.Entity<Received>().ToTable("received");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        FixtureMigrations.IgnoreMissingSnapshot(optionsBuilder);
    }
}

[DbContext(typeof(SubscriberDb))]
[Migration("20261125000000_Initial")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.EnsureSchema(SubscriberDb.SchemaName);
        m.CreateTable("received", schema: SubscriberDb.SchemaName, columns: t => new
        {
            Id = t.Column<Guid>(type: "uuid", nullable: false),
            EventId = t.Column<Guid>(type: "uuid", nullable: false),
            OrderId = t.Column<Guid>(type: "uuid", nullable: false),
            Number = t.Column<int>(type: "integer", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_received", x => x.Id));
        FixtureMigrations.CreateIntegrationTables(m, SubscriberDb.SchemaName);
    }
}

/// <summary>The subscriber's own copy of the publisher's event.</summary>
public sealed record OrderPlacedCopy(Guid OrderId, int Number);

public sealed class OrderHandler(SubscriberDb db) : IIntegrationEventHandler
{
    public Task HandleAsync(IntegrationEnvelope envelope, CancellationToken ct)
    {
        var order = envelope.DataAs<OrderPlacedCopy>();
        // A negative number is a poison event, for the dead-letter path.
        if (order.Number < 0) throw new InvalidOperationException($"Order number {order.Number} is invalid.");
        db.Received.Add(new Received { Id = Guid.NewGuid(), EventId = envelope.Id, OrderId = order.OrderId, Number = order.Number });
        return Task.CompletedTask;
    }
}
