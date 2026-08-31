using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceGroundedEvidenceExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_evidence_studies_study_id",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "claim",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "confidence",
                table: "evidence");

            migrationBuilder.RenameIndex(
                name: "IX_evidence_study_id",
                table: "evidence",
                newName: "ix_evidence_study_id");

            migrationBuilder.AddColumn<string>(
                name: "comparator",
                table: "evidence",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "confidence_interval_lower",
                table: "evidence",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "confidence_interval_upper",
                table: "evidence",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effect_measure",
                table: "evidence",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "effect_value",
                table: "evidence",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "evidence_extraction_id",
                table: "evidence",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "exposure_or_intervention",
                table: "evidence",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "extracted_at",
                table: "evidence",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<bool>(
                name: "grounding_validated",
                table: "evidence",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "outcome",
                table: "evidence",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "p_value",
                table: "evidence",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "population",
                table: "evidence",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "research_run_id",
                table: "evidence",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "result_summary",
                table: "evidence",
                type: "character varying(800)",
                maxLength: 800,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "sample_size",
                table: "evidence",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_scope",
                table: "evidence",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "study_design",
                table: "evidence",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supporting_text",
                table: "evidence",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "evidence_extractions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    skip_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evidence_count = table.Column<int>(type: "integer", nullable: false),
                    grounding_validated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_extractions", x => x.id);
                    table.ForeignKey(
                        name: "FK_evidence_extractions_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evidence_extractions_studies_study_id",
                        column: x => x.study_id,
                        principalTable: "studies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_evidence_extraction_id",
                table: "evidence",
                column: "evidence_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_research_run_id",
                table: "evidence",
                column: "research_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_extractions_research_run_id_status",
                table: "evidence_extractions",
                columns: new[] { "research_run_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_extractions_study_id",
                table: "evidence_extractions",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ux_evidence_extractions_research_run_id_study_id_prompt_version",
                table: "evidence_extractions",
                columns: new[] { "research_run_id", "study_id", "prompt_version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_evidence_extractions_evidence_extraction_id",
                table: "evidence",
                column: "evidence_extraction_id",
                principalTable: "evidence_extractions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_research_runs_research_run_id",
                table: "evidence",
                column: "research_run_id",
                principalTable: "research_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_studies_study_id",
                table: "evidence",
                column: "study_id",
                principalTable: "studies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_evidence_evidence_extractions_evidence_extraction_id",
                table: "evidence");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_research_runs_research_run_id",
                table: "evidence");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_studies_study_id",
                table: "evidence");

            migrationBuilder.DropTable(
                name: "evidence_extractions");

            migrationBuilder.DropIndex(
                name: "ix_evidence_evidence_extraction_id",
                table: "evidence");

            migrationBuilder.DropIndex(
                name: "ix_evidence_research_run_id",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "comparator",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "confidence_interval_lower",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "confidence_interval_upper",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "effect_measure",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "effect_value",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "evidence_extraction_id",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "exposure_or_intervention",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "extracted_at",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "grounding_validated",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "outcome",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "p_value",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "population",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "research_run_id",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "result_summary",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "sample_size",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "source_scope",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "study_design",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "supporting_text",
                table: "evidence");

            migrationBuilder.RenameIndex(
                name: "ix_evidence_study_id",
                table: "evidence",
                newName: "IX_evidence_study_id");

            migrationBuilder.AddColumn<string>(
                name: "claim",
                table: "evidence",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "confidence",
                table: "evidence",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_studies_study_id",
                table: "evidence",
                column: "study_id",
                principalTable: "studies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
