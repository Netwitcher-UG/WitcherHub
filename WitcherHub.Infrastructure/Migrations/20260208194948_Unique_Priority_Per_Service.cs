using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Unique_Priority_Per_Service : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PricingRules_ServiceId_Priority",
                table: "PricingRules");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_ServiceId_Priority",
                table: "PricingRules",
                columns: new[] { "ServiceId", "Priority" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PricingRules_ServiceId_Priority",
                table: "PricingRules");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_ServiceId_Priority",
                table: "PricingRules",
                columns: new[] { "ServiceId", "Priority" });
        }
    }
}
