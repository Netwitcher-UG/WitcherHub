using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GenericCommercialTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonDocument>(
                name: "FinancialSummary",
                table: "Contracts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "CommercialTerm",
                table: "ContractItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHumanReviewed",
                table: "ContractItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinancialSummary",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "CommercialTerm",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "IsHumanReviewed",
                table: "ContractItems");
        }
    }
}
