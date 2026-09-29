using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuantitativeSynthesisArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quantitative_synthesis_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    algorithm_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    output_confidence_level = table.Column<decimal>(type: "numeric(8,7)", nullable: false),
                    evidence_count = table.Column<int>(type: "integer", nullable: false),
                    unique_study_count = table.Column<int>(type: "integer", nullable: false),
                    snapshot_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    persisted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quantitative_synthesis_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_quantitative_synthesis_artifacts_research_runs_research_run~",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quantitative_synthesis_contribution_snapshots",
                columns: table => new
                {
                    artifact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_extraction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    analysis_scale_effect = table.Column<double>(type: "double precision", nullable: false),
                    analysis_scale_variance = table.Column<double>(type: "double precision", nullable: false),
                    analysis_scale_standard_error = table.Column<double>(type: "double precision", nullable: false),
                    weight = table.Column<double>(type: "double precision", nullable: false),
                    normalized_weight = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quantitative_synthesis_contribution_snapshots", x => new { x.artifact_id, x.analysis_method, x.ordinal });
                    table.ForeignKey(
                        name: "FK_quantitative_synthesis_contribution_snapshots_evidence_evid~",
                        column: x => x.evidence_id,
                        principalTable: "evidence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quantitative_synthesis_contribution_snapshots_evidence_extr~",
                        column: x => x.evidence_extraction_id,
                        principalTable: "evidence_extractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quantitative_synthesis_contribution_snapshots_quantitative_~",
                        column: x => x.artifact_id,
                        principalTable: "quantitative_synthesis_artifacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_quantitative_synthesis_contribution_snapshots_source_materi~",
                        column: x => x.source_material_id,
                        principalTable: "source_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quantitative_synthesis_contribution_snapshots_studies_study~",
                        column: x => x.study_id,
                        principalTable: "studies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quantitative_synthesis_artifacts_run_status",
                table: "quantitative_synthesis_artifacts",
                columns: new[] { "research_run_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_quantitative_synthesis_artifacts_run_group",
                table: "quantitative_synthesis_artifacts",
                columns: new[] { "research_run_id", "group_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quantitative_synthesis_contribution_snapshots_evidence_extr~",
                table: "quantitative_synthesis_contribution_snapshots",
                column: "evidence_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_quantitative_synthesis_contribution_snapshots_evidence_id",
                table: "quantitative_synthesis_contribution_snapshots",
                column: "evidence_id");

            migrationBuilder.CreateIndex(
                name: "IX_quantitative_synthesis_contribution_snapshots_source_materi~",
                table: "quantitative_synthesis_contribution_snapshots",
                column: "source_material_id");

            migrationBuilder.CreateIndex(
                name: "IX_quantitative_synthesis_contribution_snapshots_study_id",
                table: "quantitative_synthesis_contribution_snapshots",
                column: "study_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quantitative_synthesis_contribution_snapshots");

            migrationBuilder.DropTable(
                name: "quantitative_synthesis_artifacts");
        }
    }
}
