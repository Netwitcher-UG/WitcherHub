using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuoteAccessLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuoteSignature_Quotes_QuoteId",
                table: "QuoteSignature");

            migrationBuilder.DropPrimaryKey(
                name: "PK_QuoteSignature",
                table: "QuoteSignature");

            migrationBuilder.RenameTable(
                name: "QuoteSignature",
                newName: "QuoteSignatures");

            migrationBuilder.RenameIndex(
                name: "IX_QuoteSignature_QuoteId",
                table: "QuoteSignatures",
                newName: "IX_QuoteSignatures_QuoteId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_QuoteSignatures",
                table: "QuoteSignatures",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "QuoteAccessLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastOpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteAccessLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteAccessLinks_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAccessLinks_QuoteId_RecipientEmail",
                table: "QuoteAccessLinks",
                columns: new[] { "QuoteId", "RecipientEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAccessLinks_TokenHash",
                table: "QuoteAccessLinks",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteSignatures_Quotes_QuoteId",
                table: "QuoteSignatures",
                column: "QuoteId",
                principalTable: "Quotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuoteSignatures_Quotes_QuoteId",
                table: "QuoteSignatures");

            migrationBuilder.DropTable(
                name: "QuoteAccessLinks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_QuoteSignatures",
                table: "QuoteSignatures");

            migrationBuilder.RenameTable(
                name: "QuoteSignatures",
                newName: "QuoteSignature");

            migrationBuilder.RenameIndex(
                name: "IX_QuoteSignatures_QuoteId",
                table: "QuoteSignature",
                newName: "IX_QuoteSignature_QuoteId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_QuoteSignature",
                table: "QuoteSignature",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteSignature_Quotes_QuoteId",
                table: "QuoteSignature",
                column: "QuoteId",
                principalTable: "Quotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
