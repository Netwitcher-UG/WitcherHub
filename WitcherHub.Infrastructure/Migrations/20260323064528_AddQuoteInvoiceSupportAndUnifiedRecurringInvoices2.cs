using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteInvoiceSupportAndUnifiedRecurringInvoices2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AfterCustomerSignAction",
                table: "Quotes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceSendMode",
                table: "Quotes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRecurringInvoiceRunAt",
                table: "Quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NextRecurringInvoiceDate",
                table: "Quotes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecurringEnabled",
                table: "Quotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurringEndDate",
                table: "Quotes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecurringIsActive",
                table: "Quotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurringStartDate",
                table: "Quotes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignedAt",
                table: "Quotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuoteId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuoteSignature",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SignerEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SignatureData = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteSignature", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteSignature_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuoteId",
                table: "Invoices",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteSignature_QuoteId",
                table: "QuoteSignature",
                column: "QuoteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Quotes_QuoteId",
                table: "Invoices",
                column: "QuoteId",
                principalTable: "Quotes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Quotes_QuoteId",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "QuoteSignature");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_QuoteId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "AfterCustomerSignAction",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "InvoiceSendMode",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "LastRecurringInvoiceRunAt",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "NextRecurringInvoiceDate",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "RecurringEnabled",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "RecurringEndDate",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "RecurringIsActive",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "RecurringStartDate",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "SignedAt",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "Invoices");
        }
    }
}
