using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractItemFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscountType",
                table: "ContractItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "ContractItems",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "PriceBreakdown",
                table: "ContractItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "ContractItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "ContractItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "PriceBreakdown",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "ContractItems");
        }
    }
}
