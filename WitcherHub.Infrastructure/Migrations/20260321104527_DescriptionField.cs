using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DescriptionField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Discription",
                table: "Services",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Discription",
                table: "QuoteItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Discription",
                table: "InvoiceItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Discription",
                table: "ContractItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "InvoiceAccessLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OneTimeUse = table.Column<bool>(type: "boolean", nullable: false),
                    FirstOpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastOpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OpenCount = table.Column<int>(type: "integer", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAccessLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceAccessLinks_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAccessLinks_InvoiceId_ExpiresAt",
                table: "InvoiceAccessLinks",
                columns: new[] { "InvoiceId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAccessLinks_TokenHash",
                table: "InvoiceAccessLinks",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceAccessLinks");

            migrationBuilder.DropColumn(
                name: "Discription",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "Discription",
                table: "QuoteItems");

            migrationBuilder.DropColumn(
                name: "Discription",
                table: "InvoiceItems");

            migrationBuilder.DropColumn(
                name: "Discription",
                table: "ContractItems");
        }
    }
}
