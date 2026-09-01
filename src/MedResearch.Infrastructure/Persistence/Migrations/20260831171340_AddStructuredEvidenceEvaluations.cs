using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredEvidenceEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evidence_evaluations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    skip_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evaluator_provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    evaluator_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    study_design = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sample_information = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    comparator_presence = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    comparator_description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    randomization = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    blinding = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    allocation_concealment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attrition_missing_data = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    precision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    directness = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    overall_confidence = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reporting_limitations = table.Column<string[]>(type: "text[]", nullable: false),
                    author_reported_limitations = table.Column<string[]>(type: "text[]", nullable: false),
                    has_sample_size = table.Column<bool>(type: "boolean", nullable: false),
                    has_effect_estimate = table.Column<bool>(type: "boolean", nullable: false),
                    has_confidence_interval = table.Column<bool>(type: "boolean", nullable: false),
                    has_p_value = table.Column<bool>(type: "boolean", nullable: false),
                    has_comparator = table.Column<bool>(type: "boolean", nullable: false),
                    unknown_domain_count = table.Column<int>(type: "integer", nullable: false),
                    insufficient_source_domain_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_evaluations", x => x.id);
                    table.ForeignKey(
                        name: "FK_evidence_evaluations_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evidence_evaluations_studies_study_id",
                        column: x => x.study_id,
                        principalTable: "studies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_evaluations_research_run_id_status",
                table: "evidence_evaluations",
                columns: new[] { "research_run_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_evaluations_study_id",
                table: "evidence_evaluations",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ux_evidence_evaluations_research_run_id_study_id_prompt_version",
                table: "evidence_evaluations",
                columns: new[] { "research_run_id", "study_id", "prompt_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence_evaluations");
        }
    }
}
