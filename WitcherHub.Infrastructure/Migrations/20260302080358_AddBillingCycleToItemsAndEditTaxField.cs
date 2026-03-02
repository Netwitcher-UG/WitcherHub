using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingCycleToItemsAndEditTaxField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceItems_TaxRates_TaxRateId",
                table: "InvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_TaxRates_TaxRateId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_QuoteItems_TaxRates_TaxRateId",
                table: "QuoteItems");

            migrationBuilder.DropTable(
                name: "DiscountCodes");

            migrationBuilder.DropTable(
                name: "TaxRates");

            migrationBuilder.DropIndex(
                name: "IX_QuoteItems_TaxRateId",
                table: "QuoteItems");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TaxRateId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceItems_TaxRateId",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "TaxRateId",
                table: "QuoteItems");

            migrationBuilder.DropColumn(
                name: "TaxRateId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TaxRateId",
                table: "InvoiceItems");

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "QuoteItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Tax",
                table: "QuoteItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Tax",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "InvoiceItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Tax",
                table: "InvoiceItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "ContractItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Tax",
                table: "ContractItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "QuoteItems");

            migrationBuilder.DropColumn(
                name: "Tax",
                table: "QuoteItems");

            migrationBuilder.DropColumn(
                name: "Tax",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "Tax",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "Tax",
                table: "ContractItems");

            migrationBuilder.AddColumn<Guid>(
                name: "TaxRateId",
                table: "QuoteItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxRateId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxRateId",
                table: "InvoiceItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiscountCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DiscountType = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Value = table.Column<decimal>(type: "numeric(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Country = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteItems_TaxRateId",
                table: "QuoteItems",
                column: "TaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TaxRateId",
                table: "Invoices",
                column: "TaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_TaxRateId",
                table: "InvoiceItems",
                column: "TaxRateId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceItems_TaxRates_TaxRateId",
                table: "InvoiceItems",
                column: "TaxRateId",
                principalTable: "TaxRates",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_TaxRates_TaxRateId",
                table: "Invoices",
                column: "TaxRateId",
                principalTable: "TaxRates",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteItems_TaxRates_TaxRateId",
                table: "QuoteItems",
                column: "TaxRateId",
                principalTable: "TaxRates",
                principalColumn: "Id");
        }
    }
}
