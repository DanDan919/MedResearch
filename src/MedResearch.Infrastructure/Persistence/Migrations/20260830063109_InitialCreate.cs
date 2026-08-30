using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "research_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_questions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "studies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    @abstract = table.Column<string>(name: "abstract", type: "text", nullable: true),
                    doi = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    pmid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    journal = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    publication_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_studies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "research_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    research_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_runs_research_questions_research_question_id",
                        column: x => x.research_question_id,
                        principalTable: "research_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    study_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_evidence_studies_study_id",
                        column: x => x.study_id,
                        principalTable: "studies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_study_id",
                table: "evidence",
                column: "study_id");

            migrationBuilder.CreateIndex(
                name: "IX_research_runs_research_question_id",
                table: "research_runs",
                column: "research_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_research_runs_status_created_at",
                table: "research_runs",
                columns: new[] { "status", "created_at" });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence");

            migrationBuilder.DropTable(
                name: "research_runs");

            migrationBuilder.DropTable(
                name: "studies");

            migrationBuilder.DropTable(
                name: "research_questions");
        }
    }
}
