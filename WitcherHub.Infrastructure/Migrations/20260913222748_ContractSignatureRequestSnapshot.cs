using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractSignatureRequestSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                table: "ContractAccessLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsumedAtUtc",
                table: "ContractAccessLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "ContractAccessLinks",
                type: "uuid",
                nullable: true);

            // Existing rows were all created by the e-mail invitation
            // controller, which was the only path that issued a link, so that is
            // what they are backfilled as. An empty string is not a valid enum
            // name and would make every historical row fail to materialise.
            migrationBuilder.AddColumn<string>(
                name: "DeliveryMethod",
                table: "ContractAccessLinks",
                type: "text",
                nullable: false,
                defaultValue: "Email");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAtUtc",
                table: "ContractAccessLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotHash",
                table: "ContractAccessLinks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotMarkdown",
                table: "ContractAccessLinks",
                type: "text",
                nullable: true);

            // Likewise: they were sent. A row that was since revoked is still
            // refused, because RevokedAtUtc is checked separately.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ContractAccessLinks",
                type: "text",
                nullable: false,
                defaultValue: "Sent");

            migrationBuilder.AddColumn<string>(
                name: "TermsHash",
                table: "ContractAccessLinks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsUrl",
                table: "ContractAccessLinks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "ContractAccessLinks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ViewedAtUtc",
                table: "ContractAccessLinks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "ConsumedAtUtc",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "DeliveryMethod",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "SentAtUtc",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "SnapshotHash",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "SnapshotMarkdown",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "TermsHash",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "TermsUrl",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "ViewedAtUtc",
                table: "ContractAccessLinks");
        }
    }
}
