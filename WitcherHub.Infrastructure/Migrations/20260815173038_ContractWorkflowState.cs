using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractWorkflowState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedDraftId",
                table: "Contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPreparationKey",
                table: "Contracts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastPreparedDraftId",
                table: "Contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreparationState",
                table: "Contracts",
                type: "text",
                nullable: false,

                // Enum values are stored by name. The generated default of "" would
                // leave every existing row holding a value that maps to no member
                // and fails to read.
                defaultValue: "NoPreparedDraft");

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "Contracts",
                type: "text",
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<string>(
                name: "SourceState",
                table: "Contracts",
                type: "text",
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ContractDrafts",
                type: "text",
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SupersededAt",
                table: "ContractDrafts",
                type: "timestamp with time zone",
                nullable: true);

            // Existing rows already carry these facts in other columns; this only
            // writes them where they can now be read directly. Nothing is deleted
            // and no wording is rewritten.
            migrationBuilder.Sql("""
                UPDATE "ContractDrafts" SET "Status" = 'Approved' WHERE "IsApproved" = true;
                """);

            migrationBuilder.Sql("""
                UPDATE "Contracts" c SET "ApprovedDraftId" = (
                    SELECT d."Id" FROM "ContractDrafts" d
                    WHERE d."ContractId" = c."Id" AND d."IsApproved" = true
                    ORDER BY d."Version" DESC LIMIT 1);
                """);

            migrationBuilder.Sql("""
                UPDATE "Contracts" c
                SET "SourceState" = CASE
                        WHEN EXISTS (SELECT 1 FROM "ContractDrafts" d
                                     WHERE d."ContractId" = c."Id" AND d."ExtractionStatus" = 'Analysed')
                            THEN 'Analysed'
                        WHEN EXISTS (SELECT 1 FROM "ContractDrafts" d WHERE d."ContractId" = c."Id")
                            THEN 'SuppliedTextSaved'
                        ELSE 'None'
                    END,
                    "ReviewState" = CASE
                        WHEN EXISTS (SELECT 1 FROM "ContractDrafts" d
                                     WHERE d."ContractId" = c."Id" AND d."ExtractionStatus" = 'Confirmed')
                            THEN 'Confirmed'
                        WHEN EXISTS (SELECT 1 FROM "ContractDrafts" d
                                     WHERE d."ContractId" = c."Id" AND d."ExtractionStatus" = 'Analysed')
                            THEN 'RequiresReview'
                        ELSE 'NotRequired'
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedDraftId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LastPreparationKey",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "LastPreparedDraftId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PreparationState",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ReviewState",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SourceState",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "ContractDrafts");
        }
    }
}
