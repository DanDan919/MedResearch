using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickCreationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "creation_fingerprint",
                table: "quick_research_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creation_key",
                table: "quick_research_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_quick_owner_creation_key",
                table: "quick_research_runs",
                columns: new[] { "owner_subject_id", "creation_key" },
                unique: true,
                filter: "creation_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quick_creation",
                table: "quick_research_runs",
                sql: "(creation_key IS NULL AND creation_fingerprint IS NULL) OR (creation_key IS NOT NULL AND creation_key <> '00000000-0000-0000-0000-000000000000'::uuid AND creation_fingerprint IS NOT NULL AND creation_fingerprint ~ '^[0-9A-F]{64}$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_quick_owner_creation_key",
                table: "quick_research_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quick_creation",
                table: "quick_research_runs");

            migrationBuilder.DropColumn(
                name: "creation_fingerprint",
                table: "quick_research_runs");

            migrationBuilder.DropColumn(
                name: "creation_key",
                table: "quick_research_runs");
        }
    }
}
