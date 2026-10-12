using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickBoundedRetrieval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_quick_result",
                table: "quick_research_runs");

            migrationBuilder.AddColumn<string>(
                name: "retrieval",
                table: "quick_research_runs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_quick_result",
                table: "quick_research_runs",
                sql: "(status = 'Completed' AND ((result IS NOT NULL AND retrieval IS NULL AND COALESCE(result ->> 'QuickRunId' = id::text AND result ->> 'IsSynthetic' = 'true', FALSE)) OR (result IS NULL AND retrieval IS NOT NULL AND COALESCE(retrieval ->> 'IsFinal' = 'true', FALSE)))) OR (status <> 'Completed' AND result IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quick_retrieval",
                table: "quick_research_runs",
                sql: "retrieval IS NULL OR (status <> 'Queued' AND result IS NULL AND COALESCE(retrieval ->> 'QuickRunId' = id::text, FALSE))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_quick_result",
                table: "quick_research_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quick_retrieval",
                table: "quick_research_runs");

            migrationBuilder.DropColumn(
                name: "retrieval",
                table: "quick_research_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quick_result",
                table: "quick_research_runs",
                sql: "(status = 'Completed' AND result IS NOT NULL AND COALESCE(result ->> 'QuickRunId' = id::text AND result ->> 'IsSynthetic' = 'true', FALSE)) OR (status <> 'Completed' AND result IS NULL)");
        }
    }
}
