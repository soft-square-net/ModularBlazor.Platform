using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data.Messaging;

namespace soft.square.net.WebApi.Data
{
    public static class IntegrationEventsServiceCollectionExtensions
    {
        /// <summary>
        /// Integration events for a module whose <typeparamref name="TContext"/> maps the outbox and inbox
        /// (<c>protected override bool UsesIntegrationEvents =&gt; true</c>, plus its migration). Everything is registered in
        /// the module's container, so it stops with the module:
        /// <list type="bullet">
        /// <item><see cref="IIntegrationEvents"/> (scoped): <c>Publish</c> adds an outbox row to the scope's <typeparamref name="TContext"/>;</item>
        /// <item>an interceptor that hands the rows of a committed save (or transaction) to the outbox sender at once;</item>
        /// <item>the outbox sweeper (a hosted service): sends those rows, and resends rows left unsent, for each active tenant;</item>
        /// <item>the inbox (<see cref="IIntegrationMessageProcessor"/>) the host calls for each event the module subscribes to.</item>
        /// </list>
        /// Handlers: <c>services.AddIntegrationHandler&lt;T&gt;(pattern)</c>, with the pattern in the manifest's Subscriptions.
        /// </summary>
        public static IServiceCollection AddIntegrationEvents<TContext>(this IServiceCollection services, Action<IntegrationEventsOptions>? configure = null)
            where TContext : TenantDbContext
        {
            var options = new IntegrationEventsOptions();
            configure?.Invoke(options);
            services.AddSingleton(new IntegrationEventsSettings<TContext>(options));

            services.TryAddScoped<IntegrationEventContext>();
            services.TryAddScoped<IIntegrationEventContext>(sp => sp.GetRequiredService<IntegrationEventContext>());
            services.AddScoped<IIntegrationEvents, IntegrationEvents<TContext>>();
            services.AddScoped<IIntegrationMessageProcessor, IntegrationInbox<TContext>>();

            services.AddSingleton<OutboxDispatcher<TContext>>();
            services.AddScoped<OutboxInterceptor<TContext>>();
            services.ConfigureDbContext<TContext>((sp, builder) => builder.AddInterceptors(sp.GetRequiredService<OutboxInterceptor<TContext>>()));
            services.AddHostedService<OutboxSweeper<TContext>>();
            return services;
        }
    }
}

namespace soft.square.net.WebApi.Data.Messaging
{
    public sealed class IntegrationEventsOptions
    {
        /// <summary>The publishing module's id in <see cref="IntegrationEnvelope.Source"/>; the context's schema when not set.</summary>
        public string? Source { get; set; }

        /// <summary>How often the sweeper looks for unsent rows in every active tenant.</summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Rows younger than this are left to the after-commit send (so the sweeper does not race it).</summary>
        public TimeSpan SweepDelay { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Sent outbox rows and inbox rows are deleted after this (the inbox's duplicate window).</summary>
        public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

        public int BatchSize { get; set; } = 100;
    }

    /// <summary>The options of one context's integration events (a module container singleton).</summary>
    public sealed record IntegrationEventsSettings<TContext>(IntegrationEventsOptions Options) where TContext : TenantDbContext;

    /// <summary>Scoped: the event being handled, so events published by its handlers share its correlation id.</summary>
    public sealed class IntegrationEventContext : IIntegrationEventContext
    {
        public IntegrationEnvelope? Current { get; set; }
    }

    internal static class IntegrationGuard
    {
        public static void EnsureMapped(TenantDbContext db)
        {
            if (!db.MapsIntegrationTables)
                throw new InvalidOperationException(
                    $"{db.GetType().Name} does not map the integration outbox and inbox: override UsesIntegrationEvents to return true and add a migration.");
        }
    }

    /// <summary>Adds the event to the outbox of the scope's <typeparamref name="TContext"/>; sent once that save commits.</summary>
    public sealed class IntegrationEvents<TContext>(TContext db, IIntegrationEventContext context, IntegrationEventsSettings<TContext> settings)
        : IIntegrationEvents where TContext : TenantDbContext
    {
        public IntegrationEnvelope Publish(string type, object data, string subject, string? unitPath = null, string? orgId = null)
        {
            IntegrationTypes.ValidateType(type);
            ArgumentNullException.ThrowIfNull(data);
            IntegrationGuard.EnsureMapped(db);
            var tenant = db.CurrentTenantId ?? throw new InvalidOperationException("Integration events can only be published in a tenant scope.");

            var id = Guid.CreateVersion7();
            var correlation = context.Current?.CorrelationId
                              ?? (Activity.Current is { } activity ? activity.TraceId.ToHexString() : id.ToString("n"));
            var envelope = new IntegrationEnvelope(id, type, tenant, subject, DateTimeOffset.UtcNow, correlation,
                settings.Options.Source ?? db.SchemaName, JsonSerializer.SerializeToElement(data, data.GetType(), IntegrationEnvelope.JsonOptions),
                orgId, unitPath);
            db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Id = id,
                Type = type,
                TenantId = tenant,
                Envelope = JsonSerializer.Serialize(envelope, IntegrationEnvelope.JsonOptions),
                OccurredAt = envelope.OccurredAt
            });
            return envelope;
        }
    }

    /// <summary>
    /// Watches the scope's <typeparamref name="TContext"/>s: outbox rows written by a save are handed to the sender once
    /// they are committed (right after the save, or when the save's transaction commits), and dropped on a rollback.
    /// Only memory work happens here; the send runs on the sweeper's loop.
    /// </summary>
    public sealed class OutboxInterceptor<TContext>(OutboxDispatcher<TContext> dispatcher) : ISaveChangesInterceptor, IDbTransactionInterceptor
        where TContext : TenantDbContext
    {
        private readonly Dictionary<DbContext, Pending> _pending = new(ReferenceEqualityComparer.Instance);

        private sealed class Pending
        {
            public List<OutboxMessage> Committed { get; } = []; // saved in the open transaction
            public List<OutboxMessage> Saving { get; } = [];   // the save in progress
        }

        public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Capture(eventData.Context);
            return result;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Capture(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Saved(eventData.Context);
            return result;
        }

        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Saved(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public void SaveChangesFailed(DbContextErrorEventData eventData) => Failed(eventData.Context);

        public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Failed(eventData.Context);
            return Task.CompletedTask;
        }

        public void SaveChangesCanceled(DbContextEventData eventData) => Failed(eventData.Context);

        public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
        {
            Failed(eventData.Context);
            return Task.CompletedTask;
        }

        public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Flush(eventData.Context);

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Flush(eventData.Context);
            return Task.CompletedTask;
        }

        public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Drop(eventData.Context);

        public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Drop(eventData.Context);
            return Task.CompletedTask;
        }

        public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => Drop(eventData.Context);

        public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Drop(eventData.Context);
            return Task.CompletedTask;
        }

        private void Capture(DbContext? context)
        {
            if (context is not TContext) return;
            var rows = context.ChangeTracker.Entries<OutboxMessage>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            if (rows.Count == 0) return;
            lock (_pending)
            {
                var pending = _pending.TryGetValue(context, out var existing) ? existing : _pending[context] = new Pending();
                pending.Saving.Clear();
                pending.Saving.AddRange(rows);
            }
        }

        private void Saved(DbContext? context)
        {
            if (context is null) return;
            lock (_pending)
            {
                if (!_pending.TryGetValue(context, out var pending)) return;
                pending.Committed.AddRange(pending.Saving);
                pending.Saving.Clear();
            }
            if (context.Database.CurrentTransaction is null) Flush(context);
        }

        private void Failed(DbContext? context)
        {
            if (context is null) return;
            lock (_pending)
                if (_pending.TryGetValue(context, out var pending)) pending.Saving.Clear(); // still Added: the next save captures them again
        }

        private void Flush(DbContext? context)
        {
            if (context is null) return;
            List<OutboxMessage> rows;
            lock (_pending)
            {
                if (!_pending.Remove(context, out var pending)) return;
                // A commit during a save is the save's own transaction (no explicit one): its rows are committed now.
                rows = [.. pending.Committed, .. pending.Saving];
                if (rows.Count == 0) return;
            }
            dispatcher.Enqueue(rows.DistinctBy(r => r.Id).ToArray());
        }

        private void Drop(DbContext? context)
        {
            if (context is null) return;
            lock (_pending) _pending.Remove(context);
        }
    }

    /// <summary>Committed outbox rows waiting for the sweeper's send loop (a module singleton).</summary>
    public sealed class OutboxDispatcher<TContext> where TContext : TenantDbContext
    {
        private readonly Channel<OutboxMessage[]> _queue = Channel.CreateUnbounded<OutboxMessage[]>(new UnboundedChannelOptions { SingleReader = true });

        public ChannelReader<OutboxMessage[]> Reader => _queue.Reader;

        /// <summary>Rows not sent from here (the module unloaded first) stay unsent in the table: the sweeper resends them.</summary>
        public void Enqueue(OutboxMessage[] rows)
        {
            if (rows.Length > 0) _queue.Writer.TryWrite(rows);
        }
    }

    /// <summary>
    /// Sends the outbox of <typeparamref name="TContext"/> (a hosted service of the module, stopped on unload):
    /// <list type="bullet">
    /// <item>right after commit: the rows the interceptor hands over;</item>
    /// <item>every <see cref="IntegrationEventsOptions.SweepInterval"/>: rows still unsent in each active tenant (the
    /// broker was down, the replica stopped between commit and send), oldest first;</item>
    /// <item>and it deletes sent outbox rows and inbox rows older than <see cref="IntegrationEventsOptions.Retention"/>.</item>
    /// </list>
    /// A row is marked sent after the broker confirmed it; a crash in between sends it again, and the subscriber's inbox
    /// drops the duplicate.
    /// </summary>
    public sealed class OutboxSweeper<TContext>(
        IServiceProvider services, OutboxDispatcher<TContext> dispatcher, IBrokerPublisher publisher, ITenantDirectory directory,
        IntegrationEventsSettings<TContext> settings, ILogger<OutboxSweeper<TContext>> log) : BackgroundService
        where TContext : TenantDbContext
    {
        private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;
        private IntegrationEventsOptions Options => settings.Options;

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.WhenAll(SendCommittedAsync(stoppingToken), SweepAsync(stoppingToken));

        private async Task SendCommittedAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var rows in dispatcher.Reader.ReadAllAsync(ct))
                foreach (var tenant in rows.GroupBy(r => r.TenantId))
                {
                    try { await SendAsync(tenant.Key, tenant.ToList(), ct); }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        log.LogWarning(e, "Sending {Count} committed integration events of tenant {Tenant} failed; the sweeper retries", tenant.Count(), tenant.Key);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task SweepAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(Options.SweepInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    var cleanup = DateTimeOffset.UtcNow - _lastCleanup > TimeSpan.FromHours(1);
                    foreach (var tenant in directory.All.Where(t => t.IsActive))
                    {
                        try { await SweepTenantAsync(tenant, cleanup, ct); }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            // Typically: the tenant's migration has not created the outbox yet.
                            log.LogDebug(e, "Outbox sweep of {Context} for tenant {Tenant} failed", typeof(TContext).Name, tenant.Id);
                        }
                    }
                    if (cleanup) _lastCleanup = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>Sends the tenant's unsent rows older than the sweep delay; returns how many were found.</summary>
        public async Task<int> SweepTenantAsync(TenantInfo tenant, bool cleanup, CancellationToken ct)
        {
            List<OutboxMessage> rows;
            await using (var scope = services.CreateTenantScope(tenant))
            {
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                var before = DateTimeOffset.UtcNow - Options.SweepDelay;
                rows = await db.Set<OutboxMessage>().AsNoTracking()
                    .Where(m => m.SentAt == null && m.OccurredAt < before)
                    .OrderBy(m => m.OccurredAt)
                    .Take(Options.BatchSize)
                    .ToListAsync(ct);

                if (cleanup)
                {
                    var expired = DateTimeOffset.UtcNow - Options.Retention;
                    await db.Set<OutboxMessage>().Where(m => m.SentAt != null && m.SentAt < expired).ExecuteDeleteAsync(ct);
                    await db.Set<InboxMessage>().Where(m => m.ProcessedAt < expired).ExecuteDeleteAsync(ct);
                }
            }
            if (rows.Count > 0)
            {
                log.LogInformation("Resending {Count} unsent integration events of {Context} for tenant {Tenant}", rows.Count, typeof(TContext).Name, tenant.Id);
                await SendAsync(tenant.Id, rows, ct);
            }
            return rows.Count;
        }

        private async Task SendAsync(string tenantId, IReadOnlyList<OutboxMessage> rows, CancellationToken ct)
        {
            var tenant = directory.Find(tenantId);
            if (tenant is null) return;

            var sent = new List<Guid>();
            var failed = new List<(Guid Id, string Error)>();
            foreach (var row in rows)
            {
                try
                {
                    await publisher.PublishAsync(new IntegrationMessage(row.Id, row.Type, row.TenantId, Encoding.UTF8.GetBytes(row.Envelope)), ct);
                    sent.Add(row.Id);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "Integration event {Id} ({Type}) of tenant {Tenant} could not be sent", row.Id, row.Type, tenantId);
                    failed.Add((row.Id, e.Message.Length > 1000 ? e.Message[..1000] : e.Message));
                }
            }

            await using var scope = services.CreateTenantScope(tenant);
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var now = DateTimeOffset.UtcNow;
            if (sent.Count > 0)
                await db.Set<OutboxMessage>().Where(m => sent.Contains(m.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.SentAt, now), ct);
            foreach (var (id, error) in failed)
                await db.Set<OutboxMessage>().Where(m => m.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Attempts, m => m.Attempts + 1).SetProperty(m => m.LastError, error), ct);
        }
    }

    /// <summary>
    /// The module's inbox (scoped, resolved by the host per message from a tenant scope of the module's container): in
    /// one transaction, it checks that the event was not handled yet, runs every handler whose pattern matches its type,
    /// and saves their changes with the inbox row. A concurrent second delivery fails on the inbox key and is reported
    /// as a duplicate.
    /// </summary>
    public sealed class IntegrationInbox<TContext>(TContext db, IServiceProvider services, IntegrationEventContext context, ILogger<IntegrationInbox<TContext>> log)
        : IIntegrationMessageProcessor where TContext : TenantDbContext
    {
        public async Task<IntegrationProcessResult> ProcessAsync(IntegrationEnvelope envelope, CancellationToken ct)
        {
            IntegrationGuard.EnsureMapped(db);
            if (!string.Equals(db.CurrentTenantId, envelope.TenantId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Integration event {envelope.Id} is for tenant '{envelope.TenantId}', but the scope is tenant '{db.CurrentTenantId}'.");

            var handlers = services.GetServices<IntegrationHandlerRegistration>()
                .Where(r => IntegrationTypes.Matches(r.Pattern, envelope.Type))
                .Select(r => r.HandlerType)
                .Distinct()
                .ToList();

            context.Current = envelope;
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async token =>
                {
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(token);
                    if (await db.Set<InboxMessage>().AsNoTracking().AnyAsync(m => m.Id == envelope.Id, token))
                        return IntegrationProcessResult.Duplicate;

                    foreach (var type in handlers)
                        await ((IIntegrationEventHandler)services.GetRequiredService(type)).HandleAsync(envelope, token);

                    db.Set<InboxMessage>().Add(new InboxMessage
                    {
                        Id = envelope.Id,
                        Type = envelope.Type,
                        Source = envelope.Source.Length > 100 ? envelope.Source[..100] : envelope.Source,
                        ProcessedAt = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                    return handlers.Count == 0 ? IntegrationProcessResult.NoHandler : IntegrationProcessResult.Handled;
                }, ct);
            }
            catch (DbUpdateException e)
            {
                if (!await IsHandledAsync(envelope.Id, ct)) throw;
                log.LogInformation(e, "Integration event {Id} ({Type}) was handled concurrently elsewhere; dropped", envelope.Id, envelope.Type);
                return IntegrationProcessResult.Duplicate;
            }
            finally
            {
                context.Current = null;
            }
        }

        private async Task<bool> IsHandledAsync(Guid id, CancellationToken ct)
        {
            db.ChangeTracker.Clear();
            return await db.Set<InboxMessage>().AsNoTracking().AnyAsync(m => m.Id == id, ct);
        }
    }
}
