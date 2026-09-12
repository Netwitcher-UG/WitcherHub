using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SigningLinkTracksApprovedVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IssuedForDraftVersion",
                table: "ContractAccessLinks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RevokedBecauseWordingChanged",
                table: "ContractAccessLinks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssuedForDraftVersion",
                table: "ContractAccessLinks");

            migrationBuilder.DropColumn(
                name: "RevokedBecauseWordingChanged",
                table: "ContractAccessLinks");
        }
    }
}
