using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractDraftGenerationReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonDocument>(
                name: "GenerationReport",
                table: "ContractDrafts",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GenerationReport",
                table: "ContractDrafts");
        }
    }
}
