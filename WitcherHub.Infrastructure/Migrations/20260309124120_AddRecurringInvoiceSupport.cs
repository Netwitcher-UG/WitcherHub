using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringInvoiceSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DispatchStatus",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsRecurringInvoice",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OriginType",
                table: "Invoices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurringCycleDate",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurringCycleKey",
                table: "Invoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRecurringInvoiceRunAt",
                table: "Contracts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NextRecurringInvoiceDate",
                table: "Contracts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecurringEnabled",
                table: "Contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurringEndDate",
                table: "Contracts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecurringIsActive",
                table: "Contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurringStartDate",
                table: "Contracts",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispatchStatus",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IsRecurringInvoice",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "OriginType",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RecurringCycleDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RecurringCycleKey",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LastRecurringInvoiceRunAt",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "NextRecurringInvoiceDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RecurringEnabled",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RecurringEndDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RecurringIsActive",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "RecurringStartDate",
                table: "Contracts");
        }
    }
}
