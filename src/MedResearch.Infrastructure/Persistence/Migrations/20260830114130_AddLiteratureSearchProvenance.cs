using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiteratureSearchProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_studies_doi",
                table: "studies");

            migrationBuilder.DropIndex(
                name: "ix_studies_pmid",
                table: "studies");

            migrationBuilder.AddColumn<string[]>(
                name: "authors",
                table: "studies",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<int>(
                name: "publication_day",
                table: "studies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "publication_month",
                table: "studies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "publication_types",
                table: "studies",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<int>(
                name: "publication_year",
                table: "studies",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "literature_searches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    query = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    searched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result_count = table.Column<int>(type: "integer", nullable: false),
                    persisted_study_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_study_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_literature_searches", x => x.id);
                    table.ForeignKey(
                        name: "FK_literature_searches_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "research_study_discoveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    literature_search_id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_study_identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_study_discoveries", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_study_discoveries_literature_searches_literature_s~",
                        column: x => x.literature_search_id,
                        principalTable: "literature_searches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_research_study_discoveries_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_research_study_discoveries_studies_study_id",
                        column: x => x.study_id,
                        principalTable: "studies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_studies_doi",
                table: "studies",
                column: "doi",
                unique: true,
                filter: "doi IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_studies_pmid",
                table: "studies",
                column: "pmid",
                unique: true,
                filter: "pmid IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_literature_searches_research_run_id_searched_at",
                table: "literature_searches",
                columns: new[] { "research_run_id", "searched_at" });

            migrationBuilder.CreateIndex(
                name: "ix_research_study_discoveries_literature_search_id",
                table: "research_study_discoveries",
                column: "literature_search_id");

            migrationBuilder.CreateIndex(
                name: "ix_research_study_discoveries_study_id",
                table: "research_study_discoveries",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "ux_research_study_discoveries_research_run_id_study_id",
                table: "research_study_discoveries",
                columns: new[] { "research_run_id", "study_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_study_discoveries");

            migrationBuilder.DropTable(
                name: "literature_searches");

            migrationBuilder.DropIndex(
                name: "ux_studies_doi",
                table: "studies");

            migrationBuilder.DropIndex(
                name: "ux_studies_pmid",
                table: "studies");

            migrationBuilder.DropColumn(
                name: "authors",
                table: "studies");

            migrationBuilder.DropColumn(
                name: "publication_day",
                table: "studies");

            migrationBuilder.DropColumn(
                name: "publication_month",
                table: "studies");

            migrationBuilder.DropColumn(
                name: "publication_types",
                table: "studies");

            migrationBuilder.DropColumn(
                name: "publication_year",
                table: "studies");

            migrationBuilder.CreateIndex(
                name: "ix_studies_doi",
                table: "studies",
                column: "doi",
                filter: "doi IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_studies_pmid",
                table: "studies",
                column: "pmid",
                filter: "pmid IS NOT NULL");
        }
    }
}
