using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tax",
                table: "QuoteItems");

            migrationBuilder.DropColumn(
                name: "Tax",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "Tax",
                table: "ContractItems");

            migrationBuilder.RenameColumn(
                name: "Tax",
                table: "Invoices",
                newName: "ApplyVat");

            migrationBuilder.AddColumn<bool>(
                name: "ApplyVat",
                table: "Quotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ApplyVat",
                table: "Contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplyVat",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ApplyVat",
                table: "Contracts");

            migrationBuilder.RenameColumn(
                name: "ApplyVat",
                table: "Invoices",
                newName: "Tax");

            migrationBuilder.AddColumn<bool>(
                name: "Tax",
                table: "QuoteItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Tax",
                table: "InvoiceItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Tax",
                table: "ContractItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
