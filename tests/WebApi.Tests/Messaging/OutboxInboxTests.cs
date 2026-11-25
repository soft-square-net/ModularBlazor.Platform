using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Data.Messaging;

namespace soft.square.net.WebApi.Tests.Messaging;

/// <summary>The module side of integration events against SQLite in memory: outbox, after-commit hand-over, sweeper, inbox.</summary>
public sealed class OutboxInboxTests : IAsyncLifetime
{
    private static readonly TenantInfo Acme = new("acme", "Acme", TenantStatus.Active, [], "tenant-data", "acme");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RecordingPublisher _publisher = new();
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantDirectory>(new Directory(Acme));
        services.AddScoped<TestTenant>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TestTenant>());
        services.AddScoped<ITenantScopeInitializer>(sp => sp.GetRequiredService<TestTenant>());
        services.AddDbContext<EventsDb>(o => o.UseSqlite(_connection));
        services.AddSingleton<IBrokerPublisher>(_publisher);
        services.AddSingleton<HandlerLog>();
        services.AddIntegrationEvents<EventsDb>(o =>
        {
            o.Source = "events";
            o.SweepDelay = TimeSpan.Zero;
        });
        services.AddIntegrationHandler<NoteHandler>("test.note-*.v1");
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EventsDb>().Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Publish_writes_the_outbox_row_in_the_same_save()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventsDb>();
            db.Notes.Add(new Note { Id = 1, Text = "hello" });
            var envelope = scope.ServiceProvider.GetRequiredService<IIntegrationEvents>().Publish("test.note-added.v1", new { id = 1 }, "note:1");

            envelope.TenantId.ShouldBe("acme");
            envelope.Source.ShouldBe("events");
            (await CountOutboxAsync()).ShouldBe(0); // nothing before the save
            Handed().ShouldBeEmpty();

            await db.SaveChangesAsync();
        }

        (await CountOutboxAsync()).ShouldBe(1);
        Handed().Single().Type.ShouldBe("test.note-added.v1");
    }

    [Fact]
    public async Task Nothing_is_written_or_handed_over_when_the_transaction_rolls_back()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventsDb>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Notes.Add(new Note { Id = 2, Text = "rolled back" });
            scope.ServiceProvider.GetRequiredService<IIntegrationEvents>().Publish("test.note-added.v1", new { id = 2 }, "note:2");
            await db.SaveChangesAsync();
            Handed().ShouldBeEmpty(); // not before the commit
            await transaction.RollbackAsync();
        }

        (await CountOutboxAsync()).ShouldBe(0);
        Handed().ShouldBeEmpty();
        _publisher.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rows_are_handed_over_when_the_transaction_commits()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventsDb>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        scope.ServiceProvider.GetRequiredService<IIntegrationEvents>().Publish("test.note-added.v1", new { id = 3 }, "note:3");
        await db.SaveChangesAsync();
        Handed().ShouldBeEmpty();

        await transaction.CommitAsync();

        Handed().Single().Type.ShouldBe("test.note-added.v1");
    }

    [Fact]
    public async Task The_sweeper_sends_unsent_rows_and_marks_them_sent()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventsDb>();
            scope.ServiceProvider.GetRequiredService<IIntegrationEvents>().Publish("test.note-added.v1", new { id = 4 }, "note:4");
            await db.SaveChangesAsync();
        }

        var sweeper = _services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<OutboxSweeper<EventsDb>>().Single();
        (await sweeper.SweepTenantAsync(Acme, cleanup: false, CancellationToken.None)).ShouldBe(1);

        var sent = _publisher.Messages.ShouldHaveSingleItem();
        sent.TenantId.ShouldBe("acme");
        IntegrationEnvelope.Parse(sent.Body.Span).Subject.ShouldBe("note:4");
        await using (var scope = _services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<EventsDb>().Set<OutboxMessage>().SingleAsync()).SentAt.ShouldNotBeNull();
        (await sweeper.SweepTenantAsync(Acme, cleanup: false, CancellationToken.None)).ShouldBe(0); // sent once
    }

    [Fact]
    public async Task The_inbox_runs_handlers_once_per_event()
    {
        var envelope = Envelope("test.note-added.v1");

        (await ProcessAsync(envelope)).ShouldBe(IntegrationProcessResult.Handled);
        (await ProcessAsync(envelope)).ShouldBe(IntegrationProcessResult.Duplicate);

        _services.GetRequiredService<HandlerLog>().Handled.ShouldBe([envelope.Id]);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventsDb>();
        (await db.Notes.CountAsync(n => n.Text == envelope.Id.ToString())).ShouldBe(1); // the handler's change was saved
        (await db.Set<InboxMessage>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_handler_leaves_neither_its_changes_nor_the_inbox_row()
    {
        var envelope = Envelope("test.note-failing.v1");

        await Should.ThrowAsync<InvalidOperationException>(() => ProcessAsync(envelope));

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventsDb>();
        (await db.Notes.CountAsync()).ShouldBe(0);
        (await db.Set<InboxMessage>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Events_a_handler_publishes_share_the_correlation_and_are_sent_after_commit()
    {
        var envelope = Envelope("test.note-chained.v1");

        await ProcessAsync(envelope);

        var chained = Handed().Single();
        IntegrationEnvelope.Parse(chained.Envelope).CorrelationId.ShouldBe(envelope.CorrelationId);
    }

    [Fact]
    public async Task An_event_without_a_matching_handler_is_marked_handled()
    {
        (await ProcessAsync(Envelope("test.other.v1"))).ShouldBe(IntegrationProcessResult.NoHandler);
    }

    private async Task<IntegrationProcessResult> ProcessAsync(IntegrationEnvelope envelope)
    {
        await using var scope = _services.CreateTenantScope(Acme);
        return await scope.ServiceProvider.GetRequiredService<IIntegrationMessageProcessor>().ProcessAsync(envelope, CancellationToken.None);
    }

    private static IntegrationEnvelope Envelope(string type) =>
        new(Guid.NewGuid(), type, "acme", "note:x", DateTimeOffset.UtcNow, "corr-" + Guid.NewGuid().ToString("n"), "other",
            System.Text.Json.JsonSerializer.SerializeToElement(new { text = "x" }));

    private List<OutboxMessage> Handed()
    {
        var rows = new List<OutboxMessage>();
        var reader = _services.GetRequiredService<OutboxDispatcher<EventsDb>>().Reader;
        while (reader.TryRead(out var batch)) rows.AddRange(batch);
        return rows;
    }

    private async Task<int> CountOutboxAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EventsDb>().Set<OutboxMessage>().CountAsync();
    }

    public sealed class Note
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
    }

    public sealed class EventsDb(DbContextOptions<EventsDb> options, ITenantContext tenant) : TenantDbContext(options, tenant)
    {
        protected override string Schema => "events";
        protected override bool UsesIntegrationEvents => true;
        public DbSet<Note> Notes => Set<Note>();

        protected override void ConfigureModel(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Note>().Property(n => n.Id).ValueGeneratedOnAdd();

        // SQLite cannot compare or order DateTimeOffset values.
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    public sealed class HandlerLog
    {
        public ConcurrentQueue<Guid> Handled { get; } = new();
    }

    public sealed class NoteHandler(EventsDb db, HandlerLog log, IIntegrationEvents events) : IIntegrationEventHandler
    {
        public Task HandleAsync(IntegrationEnvelope envelope, CancellationToken ct)
        {
            db.Notes.Add(new Note { Text = envelope.Id.ToString() });
            if (envelope.Type == "test.note-failing.v1") throw new InvalidOperationException("handler failed");
            if (envelope.Type == "test.note-chained.v1") events.Publish("test.chained.v1", new { from = envelope.Id }, "note:chained");
            log.Handled.Enqueue(envelope.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class TestTenant : ITenantContext, ITenantScopeInitializer
    {
        public TenantInfo? Tenant { get; private set; } = Acme;
        public string? ResolvedBy { get; private set; } = "test";

        public void Use(TenantInfo tenant, string resolvedBy) => (Tenant, ResolvedBy) = (tenant, resolvedBy);
    }

    private sealed class Directory(params TenantInfo[] tenants) : ITenantDirectory
    {
        public IReadOnlyCollection<TenantInfo> All => tenants;
        public TenantInfo? Find(string id) => tenants.FirstOrDefault(t => t.Id == id);
        public TenantInfo? FindByHost(string host) => null;
        public Task NotifyChangedAsync(string tenantId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingPublisher : IBrokerPublisher
    {
        public ConcurrentQueue<IntegrationMessage> Messages { get; } = new();

        public Task PublishAsync(IntegrationMessage message, CancellationToken ct)
        {
            Messages.Enqueue(message);
            return Task.CompletedTask;
        }
    }
}
