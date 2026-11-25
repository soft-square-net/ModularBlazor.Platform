using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soft.square.net.WebApi.Data.TenantStore.Migrations.Oracle
{
    /// <inheritdoc />
    public partial class AddTenantSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeedId",
                table: "platform_tenants",
                type: "NVARCHAR2(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SeedVersion",
                table: "platform_tenants",
                type: "NUMBER(19)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_tenants_SeedId",
                table: "platform_tenants",
                column: "SeedId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_platform_tenants_SeedId",
                table: "platform_tenants");

            migrationBuilder.DropColumn(
                name: "SeedId",
                table: "platform_tenants");

            migrationBuilder.DropColumn(
                name: "SeedVersion",
                table: "platform_tenants");
        }
    }
}
