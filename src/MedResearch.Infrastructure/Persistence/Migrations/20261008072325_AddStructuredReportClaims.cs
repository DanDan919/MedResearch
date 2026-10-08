using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredReportClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "research_report_claims",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(800)",
                oldMaxLength: 800);

            migrationBuilder.AddColumn<string>(
                name: "grounding_status",
                table: "research_report_claims",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "LegacyUnverified");

            migrationBuilder.AddColumn<Guid>(
                name: "numeric_evidence_id",
                table: "research_report_claims",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "quantitative_artifact_id",
                table: "research_report_claims",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "semantic_key",
                table: "research_report_claims",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "semantics",
                table: "research_report_claims",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_research_report_claims_numeric_evidence_id",
                table: "research_report_claims",
                column: "numeric_evidence_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_report_claims_quantitative_artifact_id",
                table: "research_report_claims",
                column: "quantitative_artifact_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_report_claims_research_report_id_semantic_key",
                table: "research_report_claims",
                columns: new[] { "research_report_id", "semantic_key" },
                unique: true,
                filter: "semantic_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_report_claim_structured_authority",
                table: "research_report_claims",
                sql: "(grounding_status = 'LegacyUnverified' AND semantics IS NULL AND semantic_key IS NULL AND quantitative_artifact_id IS NULL AND numeric_evidence_id IS NULL)\nOR (grounding_status = 'StructuredValidated' AND semantics IS NOT NULL AND jsonb_typeof(semantics) = 'object'\n    AND (semantics->>'ProtocolVersion') IS NOT DISTINCT FROM 'structured-claim-v1' AND semantic_key IS NOT NULL AND length(semantic_key) = 64\n    AND (semantics->>'QuantitativeArtifactId') IS NOT DISTINCT FROM quantitative_artifact_id::text\n    AND (semantics->>'NumericEvidenceId') IS NOT DISTINCT FROM numeric_evidence_id::text)");

            migrationBuilder.AddForeignKey(
                name: "FK_research_report_claims_evidence_numeric_evidence_id",
                table: "research_report_claims",
                column: "numeric_evidence_id",
                principalTable: "evidence",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_research_report_claims_quantitative_synthesis_artifacts_qua~",
                table: "research_report_claims",
                column: "quantitative_artifact_id",
                principalTable: "quantitative_synthesis_artifacts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_research_report_claims_evidence_numeric_evidence_id",
                table: "research_report_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_research_report_claims_quantitative_synthesis_artifacts_qua~",
                table: "research_report_claims");

            migrationBuilder.DropIndex(
                name: "IX_research_report_claims_numeric_evidence_id",
                table: "research_report_claims");

            migrationBuilder.DropIndex(
                name: "IX_research_report_claims_quantitative_artifact_id",
                table: "research_report_claims");

            migrationBuilder.DropIndex(
                name: "IX_research_report_claims_research_report_id_semantic_key",
                table: "research_report_claims");

            migrationBuilder.DropCheckConstraint(
                name: "ck_report_claim_structured_authority",
                table: "research_report_claims");

            migrationBuilder.DropColumn(
                name: "grounding_status",
                table: "research_report_claims");

            migrationBuilder.DropColumn(
                name: "numeric_evidence_id",
                table: "research_report_claims");

            migrationBuilder.DropColumn(
                name: "quantitative_artifact_id",
                table: "research_report_claims");

            migrationBuilder.DropColumn(
                name: "semantic_key",
                table: "research_report_claims");

            migrationBuilder.DropColumn(
                name: "semantics",
                table: "research_report_claims");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "research_report_claims",
                type: "character varying(800)",
                maxLength: 800,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);
        }
    }
}
