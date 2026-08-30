using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredResearchPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "research_plan_id",
                table: "literature_searches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "research_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    population = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    exposure_or_intervention = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    comparator = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    outcomes = table.Column<string[]>(type: "text[]", nullable: false),
                    preferred_study_types = table.Column<string[]>(type: "text[]", nullable: false),
                    search_queries = table.Column<string[]>(type: "text[]", nullable: false),
                    exclusion_hints = table.Column<string[]>(type: "text[]", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_plans_research_questions_research_question_id",
                        column: x => x.research_question_id,
                        principalTable: "research_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_plans_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_literature_searches_research_plan_id",
                table: "literature_searches",
                column: "research_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_research_plans_research_question_id",
                table: "research_plans",
                column: "research_question_id");

            migrationBuilder.CreateIndex(
                name: "ux_research_plans_research_run_id",
                table: "research_plans",
                column: "research_run_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_literature_searches_research_plans_research_plan_id",
                table: "literature_searches",
                column: "research_plan_id",
                principalTable: "research_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_literature_searches_research_plans_research_plan_id",
                table: "literature_searches");

            migrationBuilder.DropTable(
                name: "research_plans");

            migrationBuilder.DropIndex(
                name: "ix_literature_searches_research_plan_id",
                table: "literature_searches");

            migrationBuilder.DropColumn(
                name: "research_plan_id",
                table: "literature_searches");
        }
    }
}
