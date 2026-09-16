using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceQuantitativeStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "confidence_level",
                table: "evidence",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reported_standard_error",
                table: "evidence",
                type: "numeric(18,6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confidence_level",
                table: "evidence");

            migrationBuilder.DropColumn(
                name: "reported_standard_error",
                table: "evidence");
        }
    }
}
