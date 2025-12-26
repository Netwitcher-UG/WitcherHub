using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EditColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "CustomerAddresses");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Services",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceId",
                table: "QuoteItems",
                type: "uuid",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceId",
                table: "PricingRules",
                type: "uuid",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceId",
                table: "InvoiceItems",
                type: "uuid",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullNameOrCompany",
                table: "CustomerAddresses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "CustomerAddresses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StreetNr",
                table: "CustomerAddresses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceId",
                table: "ContractItems",
                type: "uuid",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullNameOrCompany",
                table: "CustomerAddresses");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "CustomerAddresses");

            migrationBuilder.DropColumn(
                name: "StreetNr",
                table: "CustomerAddresses");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Services",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "ServiceId",
                table: "QuoteItems",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ServiceId",
                table: "PricingRules",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "ServiceId",
                table: "InvoiceItems",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "CustomerAddresses",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ServiceId",
                table: "ContractItems",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldMaxLength: 80,
                oldNullable: true);
        }
    }
}
