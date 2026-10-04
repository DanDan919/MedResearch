using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiteratureSearchExecutionIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_literature_searches_execution_key",
                table: "literature_searches",
                columns: new[] { "research_run_id", "research_plan_id", "source", "query" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_literature_searches_execution_key",
                table: "literature_searches");
        }
    }
}
