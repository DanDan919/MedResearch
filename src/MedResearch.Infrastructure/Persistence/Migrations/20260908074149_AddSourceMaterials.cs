using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_evidence_extractions_research_run_id_study_id_prompt_version",
                table: "evidence_extractions");

            migrationBuilder.AddColumn<int>(
                name: "abstract_only_study_count",
                table: "research_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "no_source_material_study_count",
                table: "research_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "structured_full_text_study_count",
                table: "research_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "source_material_id",
                table: "evidence_extractions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "source_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    retrieval_method = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_version = table.Column<int>(type: "integer", nullable: false),
                    retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    license = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    license_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    access_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    character_count = table.Column<int>(type: "integer", nullable: false),
                    was_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    section_names = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_materials", x => x.id);
                    table.ForeignKey(
                        name: "FK_source_materials_studies_study_id",
                        column: x => x.study_id,
                        principalTable: "studies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_extractions_source_material_id",
                table: "evidence_extractions",
                column: "source_material_id");

            migrationBuilder.CreateIndex(
                name: "ux_evidence_extractions_run_study_source_material_prompt_version",
                table: "evidence_extractions",
                columns: new[] { "research_run_id", "study_id", "source_material_id", "prompt_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_materials_study_id",
                table: "source_materials",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ix_source_materials_study_id_type_is_current",
                table: "source_materials",
                columns: new[] { "study_id", "type", "is_current" });

            migrationBuilder.CreateIndex(
                name: "ux_source_materials_identity_hash",
                table: "source_materials",
                columns: new[] { "study_id", "type", "provider", "provider_source_id", "content_hash" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_extractions_source_materials_source_material_id",
                table: "evidence_extractions",
                column: "source_material_id",
                principalTable: "source_materials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_evidence_extractions_source_materials_source_material_id",
                table: "evidence_extractions");

            migrationBuilder.DropTable(
                name: "source_materials");

            migrationBuilder.DropIndex(
                name: "ix_evidence_extractions_source_material_id",
                table: "evidence_extractions");

            migrationBuilder.DropIndex(
                name: "ux_evidence_extractions_run_study_source_material_prompt_version",
                table: "evidence_extractions");

            migrationBuilder.DropColumn(
                name: "abstract_only_study_count",
                table: "research_reports");

            migrationBuilder.DropColumn(
                name: "no_source_material_study_count",
                table: "research_reports");

            migrationBuilder.DropColumn(
                name: "structured_full_text_study_count",
                table: "research_reports");

            migrationBuilder.DropColumn(
                name: "source_material_id",
                table: "evidence_extractions");

            migrationBuilder.CreateIndex(
                name: "ux_evidence_extractions_research_run_id_study_id_prompt_version",
                table: "evidence_extractions",
                columns: new[] { "research_run_id", "study_id", "prompt_version" },
                unique: true);
        }
    }
}
