using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;

namespace soft.square.net.EventsFixture;

/// <summary>
/// Hand-written PostgreSQL migration helpers for the test modules. A real module generates its migration with
/// dotnet ef, which also writes the model snapshot; these fixtures have none, so they skip the pending-changes check.
/// </summary>
internal static class FixtureMigrations
{
    public static void IgnoreMissingSnapshot(DbContextOptionsBuilder options) =>
        options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

    /// <summary>The integration outbox and inbox, as TenantDbContext maps them.</summary>
    public static void CreateIntegrationTables(MigrationBuilder m, string schema)
    {
        m.CreateTable("integration_outbox", schema: schema, columns: t => new
        {
            Id = t.Column<Guid>(type: "uuid", nullable: false),
            Type = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            TenantId = t.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            Envelope = t.Column<string>(type: "text", nullable: false),
            OccurredAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            SentAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            Attempts = t.Column<int>(type: "integer", nullable: false),
            LastError = t.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
        }, constraints: t => t.PrimaryKey("PK_integration_outbox", x => x.Id));
        m.CreateIndex("IX_integration_outbox_SentAt_OccurredAt", "integration_outbox", ["SentAt", "OccurredAt"], schema);

        m.CreateTable("integration_inbox", schema: schema, columns: t => new
        {
            Id = t.Column<Guid>(type: "uuid", nullable: false),
            Type = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            Source = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            ProcessedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_integration_inbox", x => x.Id));
        m.CreateIndex("IX_integration_inbox_ProcessedAt", "integration_inbox", "ProcessedAt", schema);
    }
}
