using Microsoft.EntityFrameworkCore;

namespace soft.square.net.WebApi.Data.Messaging;

/// <summary>
/// An integration event waiting to be sent (table <c>integration_outbox</c> in the module's schema). Written by
/// <see cref="Contracts.Messaging.IIntegrationEvents.Publish"/> in the same SaveChanges as the change it describes,
/// sent after the commit, and resent by the outbox sweeper while <see cref="SentAt"/> is null.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string TenantId { get; set; } = "";
    /// <summary>The whole envelope as JSON: exactly the bytes that are published.</summary>
    public string Envelope { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>Failed sends so far (the sweeper keeps trying).</summary>
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// An integration event this module has handled (table <c>integration_inbox</c> in the module's schema), saved in the
/// same transaction as the handlers' changes: a second delivery of the same id does nothing.
/// </summary>
public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>The outbox and inbox mapping, applied by <see cref="TenantDbContext"/> when the module opts in.</summary>
public static class IntegrationTables
{
    public const string Outbox = "integration_outbox";
    public const string Inbox = "integration_inbox";

    public static void Map(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable(Outbox);
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Id).ValueGeneratedNever();
            outbox.Property(m => m.Type).HasMaxLength(200).IsRequired();
            outbox.Property(m => m.TenantId).HasMaxLength(64).IsRequired();
            outbox.Property(m => m.Envelope).IsRequired();
            outbox.Property(m => m.LastError).HasMaxLength(1000);
            outbox.HasIndex(m => new { m.SentAt, m.OccurredAt });
        });
        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable(Inbox);
            inbox.HasKey(m => m.Id);
            inbox.Property(m => m.Id).ValueGeneratedNever();
            inbox.Property(m => m.Type).HasMaxLength(200).IsRequired();
            inbox.Property(m => m.Source).HasMaxLength(100).IsRequired();
            inbox.HasIndex(m => m.ProcessedAt);
        });
    }
}
