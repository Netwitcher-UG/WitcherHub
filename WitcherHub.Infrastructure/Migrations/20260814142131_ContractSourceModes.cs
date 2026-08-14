using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractSourceModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AgreedTotalNet",
                table: "Contracts",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AgreedTotalVatRatePercent",
                table: "Contracts",
                type: "numeric(6,3)",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "PartySnapshot",
                table: "Contracts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTermsText",
                table: "Contracts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PriceDeliberatelyUnspecified",
                table: "Contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SignedDocumentHash",
                table: "Contracts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignedDraftVersion",
                table: "Contracts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceMode",
                table: "Contracts",
                type: "text",
                nullable: false,

                // Enum values are stored by name, so the generated default of ""
                // would leave every existing contract holding a value that maps to
                // no member and fails to read. Existing contracts are position
                // contracts, which is exactly what this says.
                defaultValue: "Positions");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceDraftId",
                table: "ContractItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExtractedAt",
                table: "ContractDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "ExtractedTerms",
                table: "ContractDrafts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExtractionConfirmedAt",
                table: "ContractDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionStatus",
                table: "ContractDrafts",
                type: "text",
                nullable: false,
                defaultValue: "NotAnalysed");

            migrationBuilder.AddColumn<bool>(
                name: "IsImmutableSource",
                table: "ContractDrafts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ContractDrafts",
                type: "text",
                nullable: false,
                defaultValue: "Generated");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceDraftId",
                table: "ContractDrafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLanguage",
                table: "ContractDrafts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Contract text that was already pasted in before this change is a
            // supplied source document; it was recorded as one in GeneratedBy and
            // now says so in a column. Nothing is deleted or rewritten — two flags
            // are set on rows that already carry the same fact.
            migrationBuilder.Sql("""
                UPDATE "ContractDrafts"
                SET "Kind" = 'Supplied', "IsImmutableSource" = true
                WHERE "GeneratedBy" = 'pasted';
                """);

            // A contract that already has contract text and no positions is a
            // supplied-text contract, and one with both is hybrid. This is the
            // same rule the application applies; running it once here means
            // existing contracts open in the right mode instead of being
            // reported as unfinished position contracts.
            migrationBuilder.Sql("""
                UPDATE "Contracts" c
                SET "SourceMode" = CASE
                    WHEN EXISTS (SELECT 1 FROM "ContractItems" i WHERE i."ContractId" = c."Id")
                        THEN 'Hybrid'
                    ELSE 'SuppliedText'
                END
                WHERE EXISTS (SELECT 1 FROM "ContractDrafts" d WHERE d."ContractId" = c."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgreedTotalNet",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "AgreedTotalVatRatePercent",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PartySnapshot",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PaymentTermsText",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PriceDeliberatelyUnspecified",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SignedDocumentHash",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SignedDraftVersion",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SourceMode",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SourceDraftId",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "ExtractedAt",
                table: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "ExtractedTerms",
                table: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "ExtractionConfirmedAt",
                table: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "ExtractionStatus",
                table: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "IsImmutableSource",
                table: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "SourceDraftId",
                table: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "SourceLanguage",
                table: "ContractDrafts");
        }
    }
}
