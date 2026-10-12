using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickGroundedAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "answer_generation",
                table: "quick_research_runs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_quick_answer",
                table: "quick_research_runs",
                sql: "answer_generation IS NULL OR (result IS NULL AND COALESCE(answer_generation ->> 'QuickRunId' = id::text AND retrieval ->> 'IsFinal' = 'true' AND answer_generation ->> 'AttemptCount' IN ('0','1') AND ((answer_generation ->> 'Outcome' = '0' AND status = 'Processing' AND answer_generation ->> 'AttemptCount' = '1' AND answer_generation ->> 'FinishedAt' IS NULL AND answer_generation ->> 'Answer' IS NULL) OR (answer_generation ->> 'Outcome' IN ('1','2') AND status = 'Completed' AND answer_generation ->> 'FinishedAt' IS NOT NULL AND answer_generation -> 'Answer' ->> 'QuickRunId' = id::text) OR (answer_generation ->> 'Outcome' IN ('3','4') AND status = 'Failed' AND answer_generation ->> 'FinishedAt' IS NOT NULL AND answer_generation ->> 'Answer' IS NULL)), FALSE))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_quick_answer",
                table: "quick_research_runs");

            migrationBuilder.DropColumn(
                name: "answer_generation",
                table: "quick_research_runs");
        }
    }
}
