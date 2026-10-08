using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiteratureProviderAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "literature_provider_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    query = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    result_count = table.Column<int>(type: "integer", nullable: true),
                    failure_category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    literature_search_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_literature_provider_attempts", x => x.id);
                    table.CheckConstraint("ck_provider_attempt_outcome", "(status = 'Started' AND completed_at IS NULL AND result_count IS NULL AND failure_category IS NULL AND literature_search_id IS NULL)\nOR (status IN ('SucceededWithResults', 'SucceededZeroResults') AND completed_at IS NOT NULL AND completed_at >= started_at AND result_count IS NOT NULL AND literature_search_id IS NOT NULL AND failure_category IS NULL\n    AND ((status = 'SucceededZeroResults' AND result_count = 0) OR (status = 'SucceededWithResults' AND result_count > 0)))\nOR (status IN ('Failed', 'TimedOut', 'Cancelled') AND completed_at IS NOT NULL AND completed_at >= started_at AND result_count IS NULL AND literature_search_id IS NULL AND failure_category IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_literature_provider_attempts_literature_searches_literature~",
                        column: x => x.literature_search_id,
                        principalTable: "literature_searches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_literature_provider_attempts_research_plans_research_plan_id",
                        column: x => x.research_plan_id,
                        principalTable: "research_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_literature_provider_attempts_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_literature_provider_attempts_literature_search_id",
                table: "literature_provider_attempts",
                column: "literature_search_id",
                unique: true,
                filter: "literature_search_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_literature_provider_attempts_research_plan_id",
                table: "literature_provider_attempts",
                column: "research_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_literature_provider_attempts_research_run_id_started_at",
                table: "literature_provider_attempts",
                columns: new[] { "research_run_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "literature_provider_attempts");
        }
    }
}
