using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Data.TenantStore.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddTenantSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeedId",
                schema: "platform",
                table: "tenants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SeedVersion",
                schema: "platform",
                table: "tenants",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_SeedId",
                schema: "platform",
                table: "tenants",
                column: "SeedId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenants_SeedId",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "SeedId",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "SeedVersion",
                schema: "platform",
                table: "tenants");
        }
    }
}
