using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WitcherHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProjectLifecycleAndArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedById",
                table: "Projects",
                type: "uuid",
                nullable: true);

            // ---- normalising the projects the old rule left inconsistent ----
            //
            // "Waiting" was written onto a project whenever a quote or a contract
            // inside it existed but had not been agreed. It was never a statement
            // about the project, and the delete rule keyed on the status refused
            // those projects. Every existing row carrying it has to be given a
            // real answer rather than being left in a state the enum no longer
            // has.
            //
            // A project that has a signed contract or a paid invoice was genuinely
            // live: Active. Anything else that was only "Waiting" because a
            // document existed goes back to Draft, which is what it was before a
            // document touched it. Nothing else about the project changes, and no
            // document is altered.
            migrationBuilder.Sql("""
                UPDATE "Projects" p
                SET "Status" = 'Active'
                WHERE p."Status" = 'Waiting'
                  AND (
                    EXISTS (SELECT 1 FROM "Contracts" c
                            WHERE c."ProjectId" = p."Id"
                              AND (c."SignedAt" IS NOT NULL
                                   OR c."Status" IN ('Signed', 'Accepted')))
                    OR EXISTS (SELECT 1 FROM "Invoices" i
                               WHERE i."ProjectId" = p."Id" AND i."Status" = 'Paid')
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE "Projects" SET "Status" = 'Draft' WHERE "Status" = 'Waiting';
                """);

            // Any other value outside the enum — from an older migration or a
            // hand-edited row — would fail to read at all. Draft is the safe
            // landing place: it asserts nothing about the work.
            migrationBuilder.Sql("""
                UPDATE "Projects"
                SET "Status" = 'Draft'
                WHERE "Status" NOT IN ('Draft', 'Active', 'Closed', 'Cancelled', 'OnHold');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ArchivedById",
                table: "Projects");
        }
    }
}
