using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTraceableResearchReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "research_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    insufficient_evidence_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    executive_summary = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    evidence_summary = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    conflict_summary = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    limitations_summary = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    conclusion = table.Column<string>(type: "character varying(2500)", maxLength: 2500, nullable: false),
                    synthesis_confidence = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    synthesizer_provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    synthesizer_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    discovered_study_count = table.Column<int>(type: "integer", nullable: false),
                    extracted_study_count = table.Column<int>(type: "integer", nullable: false),
                    evaluated_study_count = table.Column<int>(type: "integer", nullable: false),
                    evidence_finding_count = table.Column<int>(type: "integer", nullable: false),
                    included_study_count = table.Column<int>(type: "integer", nullable: false),
                    included_evidence_finding_count = table.Column<int>(type: "integer", nullable: false),
                    claim_count = table.Column<int>(type: "integer", nullable: false),
                    search_query_count = table.Column<int>(type: "integer", nullable: false),
                    studies_with_no_extractable_evidence = table.Column<int>(type: "integer", nullable: false),
                    studies_with_insufficient_evaluation_source = table.Column<int>(type: "integer", nullable: false),
                    potential_conflict_detected = table.Column<bool>(type: "boolean", nullable: false),
                    evidence_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    uses_abstract_level_evidence_only = table.Column<bool>(type: "boolean", nullable: false),
                    searched_sources = table.Column<string[]>(type: "text[]", nullable: false),
                    deterministic_limitations = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_reports_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "research_report_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    direction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_report_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_report_claims_research_reports_research_report_id",
                        column: x => x.research_report_id,
                        principalTable: "research_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "research_report_claim_evidence",
                columns: table => new
                {
                    research_report_claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_report_claim_evidence", x => new { x.research_report_claim_id, x.evidence_id });
                    table.ForeignKey(
                        name: "FK_research_report_claim_evidence_evidence_evidence_id",
                        column: x => x.evidence_id,
                        principalTable: "evidence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_report_claim_evidence_research_report_claims_resea~",
                        column: x => x.research_report_claim_id,
                        principalTable: "research_report_claims",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_research_report_claim_evidence_evidence_id",
                table: "research_report_claim_evidence",
                column: "evidence_id");

            migrationBuilder.CreateIndex(
                name: "ux_research_report_claim_evidence_claim_id_ordinal",
                table: "research_report_claim_evidence",
                columns: new[] { "research_report_claim_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_research_report_claims_research_report_id_ordinal",
                table: "research_report_claims",
                columns: new[] { "research_report_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_research_reports_research_run_id_status",
                table: "research_reports",
                columns: new[] { "research_run_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_research_reports_research_run_id_prompt_version",
                table: "research_reports",
                columns: new[] { "research_run_id", "prompt_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_report_claim_evidence");

            migrationBuilder.DropTable(
                name: "research_report_claims");

            migrationBuilder.DropTable(
                name: "research_reports");
        }
    }
}
