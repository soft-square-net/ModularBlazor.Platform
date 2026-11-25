using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Messaging;
using soft.square.net.WebApi.Modularity;
using soft.square.net.WebApi.Tenancy;
using Testcontainers.RabbitMq;

namespace soft.square.net.WebApi.Tests.Messaging;

/// <summary>Host integration subscriptions: handlers of the host itself, on every replica, with no tenant scope.</summary>
public sealed class HostIntegrationSubscriptionTests
{
    private static IntegrationEnvelope Envelope(string type = "tenancy.seed-changed.v1", string tenant = "", Guid? id = null) =>
        new(id ?? Guid.NewGuid(), type, tenant, "seed:acme", DateTimeOffset.UtcNow, "corr", "organization",
            JsonSerializer.SerializeToElement(new { seedId = "acme" }));

    private static ServiceProvider Handlers(Action<IServiceCollection>? more = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.Configure<IntegrationOptions>(o =>
        {
            o.MaxAttempts = 3;
            o.RetryDelay = TimeSpan.FromMilliseconds(10);
        });
        services.AddSingleton<HostIntegrationHandlers>();
        services.AddSingleton<Calls>();
        services.AddHostIntegrationHandler<RecordingHandler>("tenancy.seed-*.v1");
        more?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_handler_runs_once_per_event_id()
    {
        await using var services = Handlers();
        var handlers = services.GetRequiredService<HostIntegrationHandlers>();
        var envelope = Envelope();

        await handlers.DispatchAsync(envelope, CancellationToken.None);
        await handlers.DispatchAsync(envelope, CancellationToken.None); // a redelivery
        await handlers.DispatchAsync(Envelope(), CancellationToken.None);
        await handlers.DispatchAsync(Envelope("org.unit-created.v1"), CancellationToken.None); // not subscribed

        services.GetRequiredService<Calls>().Ids.Count.ShouldBe(2);
        services.GetRequiredService<Calls>().Ids.ShouldContain(envelope.Id);
    }

    [Fact]
    public async Task A_failing_handler_is_retried_and_its_event_is_not_marked_handled_until_it_succeeds()
    {
        await using var services = Handlers();
        var calls = services.GetRequiredService<Calls>();
        var handlers = services.GetRequiredService<HostIntegrationHandlers>();
        var envelope = Envelope();

        calls.FailNext = 2; // fails twice, succeeds on the third (and last) attempt
        await handlers.DispatchAsync(envelope, CancellationToken.None);
        calls.Attempts.ShouldBe(3);
        calls.Ids.ShouldBe([envelope.Id]);

        var dropped = Envelope();
        calls.FailNext = 3; // every attempt fails: dropped, not marked handled
        await handlers.DispatchAsync(dropped, CancellationToken.None);
        calls.Ids.ShouldNotContain(dropped.Id);
        await handlers.DispatchAsync(dropped, CancellationToken.None); // so a later delivery runs it again
        calls.Ids.ShouldContain(dropped.Id);
    }

    [Fact]
    public async Task The_local_bus_delivers_a_tenantless_event_to_the_host()
    {
        await using var services = Handlers(s => s
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<ITenantChangeBus, NullTenantChangeBus>()
            .AddSingleton<TenantDirectory>()
            .AddSingleton<ModuleTenantInitializer>()
            .AddSingleton<ModuleSubscriptions>()
            .AddSingleton<IntegrationConsumers>()
            .AddSingleton<IntegrationDelivery>()
            .AddSingleton<LocalDeadLetters>()
            .AddSingleton<LocalIntegrationBus>()
            .AddOptions<ModuleRuntimeOptions>());
        var envelope = Envelope(tenant: "");

        await services.GetRequiredService<LocalIntegrationBus>().PublishAsync(
            new IntegrationMessage(envelope.Id, envelope.Type, envelope.TenantId, envelope.ToUtf8Bytes()), CancellationToken.None);

        await Eventually(() => services.GetRequiredService<Calls>().Ids.Contains(envelope.Id));
    }

    [Fact]
    public async Task With_the_broker_every_replica_receives_the_event()
    {
        RabbitMqContainer broker;
        try
        {
            broker = new RabbitMqBuilder().WithImage("rabbitmq:4-alpine").Build();
            await broker.StartAsync();
        }
        catch (Exception e)
        {
            Assert.Skip($"Docker is not available: {e.Message}");
            return;
        }

        await using var _ = broker;
        var factory = new ConnectionFactory { Uri = new Uri(broker.GetConnectionString()) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var a = Handlers(s => s.AddSingleton(connection).AddHostedService<RabbitMqHostIntegrationSubscriber>());
        await using var b = Handlers(s => s.AddSingleton(connection).AddHostedService<RabbitMqHostIntegrationSubscriber>());
        foreach (var replica in new[] { a, b })
        foreach (var service in replica.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);

        var publisher = new RabbitMqBrokerPublisher(connection, NullLogger<RabbitMqBrokerPublisher>.Instance);
        try
        {
            // The replicas bind in the background: publish until both have one, then once more to see both get the same.
            for (var i = 0; i < 100 && (a.GetRequiredService<Calls>().Ids.IsEmpty || b.GetRequiredService<Calls>().Ids.IsEmpty); i++)
            {
                var warmUp = Envelope();
                await publisher.PublishAsync(new IntegrationMessage(warmUp.Id, warmUp.Type, "", warmUp.ToUtf8Bytes()), CancellationToken.None);
                await Task.Delay(100);
            }
            var envelope = Envelope();
            await publisher.PublishAsync(new IntegrationMessage(envelope.Id, envelope.Type, "", envelope.ToUtf8Bytes()), CancellationToken.None);

            await Eventually(() => a.GetRequiredService<Calls>().Ids.Contains(envelope.Id) && b.GetRequiredService<Calls>().Ids.Contains(envelope.Id));
        }
        finally
        {
            await publisher.DisposeAsync();
            foreach (var replica in new[] { a, b })
            foreach (var service in replica.GetServices<IHostedService>())
                await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++) await Task.Delay(20);
        condition().ShouldBeTrue();
    }

    private sealed class Calls
    {
        public ConcurrentBag<Guid> Ids { get; } = [];
        public int Attempts;
        public int FailNext;
    }

    private sealed class RecordingHandler(Calls calls) : IHostIntegrationHandler
    {
        public Task HandleAsync(IntegrationEnvelope envelope, CancellationToken ct)
        {
            Interlocked.Increment(ref calls.Attempts);
            if (calls.FailNext > 0)
            {
                calls.FailNext--;
                throw new InvalidOperationException("boom");
            }
            calls.Ids.Add(envelope.Id);
            return Task.CompletedTask;
        }
    }
}
