using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickResearchSkeleton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quick_research_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    safe_error = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quick_research_runs", x => x.id);
                    table.CheckConstraint("ck_quick_lease", "lease_version >= 0 AND ((status = 'Processing' AND lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL) OR (status <> 'Processing' AND lease_owner IS NULL AND lease_expires_at IS NULL))");
                    table.CheckConstraint("ck_quick_result", "(status = 'Completed' AND result IS NOT NULL AND COALESCE(result ->> 'QuickRunId' = id::text AND result ->> 'IsSynthetic' = 'true', FALSE)) OR (status <> 'Completed' AND result IS NULL)");
                    table.CheckConstraint("ck_quick_status", "status IN ('Queued','Processing','Completed','Failed')");
                    table.CheckConstraint("ck_quick_terminal", "((status IN ('Completed','Failed')) = (completed_at IS NOT NULL)) AND ((status = 'Failed') = (safe_error IS NOT NULL))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_quick_research_runs_owner_subject_id_created_at_id",
                table: "quick_research_runs",
                columns: new[] { "owner_subject_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_quick_research_runs_status_lease_expires_at_created_at",
                table: "quick_research_runs",
                columns: new[] { "status", "lease_expires_at", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quick_research_runs");
        }
    }
}
