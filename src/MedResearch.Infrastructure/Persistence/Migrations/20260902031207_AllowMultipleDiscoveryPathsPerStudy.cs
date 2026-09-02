using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleDiscoveryPathsPerStudy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_research_study_discoveries_research_run_id_study_id",
                table: "research_study_discoveries");

            migrationBuilder.CreateIndex(
                name: "ix_research_study_discoveries_research_run_id_study_id",
                table: "research_study_discoveries",
                columns: new[] { "research_run_id", "study_id" });

            migrationBuilder.CreateIndex(
                name: "ux_research_study_discoveries_literature_search_id_study_id",
                table: "research_study_discoveries",
                columns: new[] { "literature_search_id", "study_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_research_study_discoveries_research_run_id_study_id",
                table: "research_study_discoveries");

            migrationBuilder.DropIndex(
                name: "ux_research_study_discoveries_literature_search_id_study_id",
                table: "research_study_discoveries");

            migrationBuilder.CreateIndex(
                name: "ux_research_study_discoveries_research_run_id_study_id",
                table: "research_study_discoveries",
                columns: new[] { "research_run_id", "study_id" },
                unique: true);
        }
    }
}
