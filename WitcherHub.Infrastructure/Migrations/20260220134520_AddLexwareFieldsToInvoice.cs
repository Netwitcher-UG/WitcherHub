using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLexwareFieldsToInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LexwareInvoiceId",
                table: "Invoices",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LexwarePdfPath",
                table: "Invoices",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LexwareResourceUri",
                table: "Invoices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "LexwareSnapshot",
                table: "Invoices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LexwareSyncedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LexwareVersion",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LexwareVoucherNumber",
                table: "Invoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LexwareVoucherStatus",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LexwareInvoiceId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LexwarePdfPath",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LexwareResourceUri",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LexwareSnapshot",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LexwareSyncedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LexwareVersion",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LexwareVoucherNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LexwareVoucherStatus",
                table: "Invoices");
        }
    }
}
