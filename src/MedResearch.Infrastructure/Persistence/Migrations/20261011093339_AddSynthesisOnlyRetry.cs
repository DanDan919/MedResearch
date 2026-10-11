using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSynthesisOnlyRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "failed_stage",
                table: "research_runs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_category",
                table: "research_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "synthesis_retry_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    idempotency_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_version = table.Column<long>(type: "bigint", nullable: true),
                    generation_budget = table.Column<int>(type: "integer", nullable: false),
                    generation_calls_reserved = table.Column<int>(type: "integer", nullable: false),
                    generation_pending = table.Column<bool>(type: "boolean", nullable: false),
                    failure_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_synthesis_retry_attempts", x => x.id);
                    table.CheckConstraint("ck_synthesis_retry_budget", "generation_budget BETWEEN 1 AND 3 AND generation_calls_reserved BETWEEN 0 AND generation_budget AND (NOT generation_pending OR generation_calls_reserved > 0)");
                    table.CheckConstraint("ck_synthesis_retry_status", "status IN ('Queued','Running','Succeeded','Failed','OutcomeUnknown')");
                    table.ForeignKey(
                        name: "FK_synthesis_retry_attempts_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_synthesis_retry_attempts_research_run_id",
                table: "synthesis_retry_attempts",
                column: "research_run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "synthesis_retry_attempts");

            migrationBuilder.DropColumn(
                name: "failed_stage",
                table: "research_runs");

            migrationBuilder.DropColumn(
                name: "failure_category",
                table: "research_runs");
        }
    }
}
