using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManualContractPositionsAndDrafts : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Additive only. An earlier model change left seven CustomerAddresses
        /// columns non-nullable in the model but nullable in the database; EF wanted
        /// to fold that into this migration. It was removed deliberately — those
        /// columns hold NULLs from Lexware imports, so tightening them without a
        /// backfill would fail the deploy. That drift needs its own migration and a
        /// data audit first.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {







            migrationBuilder.AddColumn<string>(
                name: "ActivationMethod",
                table: "ContractItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "ContractItems",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeliveryDate",
                table: "ContractItems",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationPeriods",
                table: "ContractItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFree",
                table: "ContractItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PricingModelName",
                table: "ContractItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceTypeLabel",
                table: "ContractItems",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "Snapshot",
                table: "ContractItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SnapshotTakenAt",
                table: "ContractItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ContractItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "ContractItems",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRatePercent",
                table: "ContractItems",
                type: "numeric(6,3)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    DocumentMarkdown = table.Column<string>(type: "text", nullable: false),
                    PositionsSnapshot = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    TemplateVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    GeneratedBy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractDrafts_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractDrafts_ContractId_Version",
                table: "ContractDrafts",
                columns: new[] { "ContractId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractDrafts");

            migrationBuilder.DropColumn(
                name: "ActivationMethod",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "DeliveryDate",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "DurationPeriods",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "IsFree",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "PricingModelName",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "ServiceTypeLabel",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "Snapshot",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "SnapshotTakenAt",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "ContractItems");

            migrationBuilder.DropColumn(
                name: "VatRatePercent",
                table: "ContractItems");







        }
    }
}
