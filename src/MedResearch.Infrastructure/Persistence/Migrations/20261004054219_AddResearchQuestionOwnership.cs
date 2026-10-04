using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchQuestionOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "owner_subject_id",
                table: "research_questions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE research_questions SET owner_subject_id = 'legacy-unowned' WHERE owner_subject_id IS NULL OR owner_subject_id = ''; ");

            migrationBuilder.AlterColumn<string>(
                name: "owner_subject_id",
                table: "research_questions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_research_questions_owner_subject_id",
                table: "research_questions",
                column: "owner_subject_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_research_questions_owner_subject_id",
                table: "research_questions");

            migrationBuilder.DropColumn(
                name: "owner_subject_id",
                table: "research_questions");
        }
    }
}
