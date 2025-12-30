using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomarForLexware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "CustomerAddresses");

            migrationBuilder.DropColumn(
                name: "StreetNr",
                table: "CustomerAddresses");

            migrationBuilder.AddColumn<bool>(
                name: "LexwareAllowTaxFreeInvoices",
                table: "Customers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LexwareArchived",
                table: "Customers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LexwareContactId",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LexwareCustomerNumber",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LexwareOrganizationId",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LexwareSyncedAtUtc",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LexwareVersion",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "CustomerContacts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLexware",
                table: "CustomerContacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "CustomerContacts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Salutation",
                table: "CustomerContacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FullNameOrCompany",
                table: "CustomerAddresses",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "CustomerAddresses",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLexware",
                table: "CustomerAddresses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StreetRaw",
                table: "CustomerAddresses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerEmailAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerEmailAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerEmailAddresses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEmailAddresses_CustomerId",
                table: "CustomerEmailAddresses",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerEmailAddresses");

            migrationBuilder.DropColumn(
                name: "LexwareAllowTaxFreeInvoices",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LexwareArchived",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LexwareContactId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LexwareCustomerNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LexwareOrganizationId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LexwareSyncedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LexwareVersion",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "CustomerContacts");

            migrationBuilder.DropColumn(
                name: "IsLexware",
                table: "CustomerContacts");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "CustomerContacts");

            migrationBuilder.DropColumn(
                name: "Salutation",
                table: "CustomerContacts");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "CustomerAddresses");

            migrationBuilder.DropColumn(
                name: "IsLexware",
                table: "CustomerAddresses");

            migrationBuilder.DropColumn(
                name: "StreetRaw",
                table: "CustomerAddresses");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Customers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FullNameOrCompany",
                table: "CustomerAddresses",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250);

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
        }
    }
}
