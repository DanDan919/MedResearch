using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchAdmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "research_admissions",
                columns: table => new
                {
                    owner_subject_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    research_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_admissions", x => new { x.owner_subject_id, x.idempotency_key });
                    table.ForeignKey(
                        name: "FK_research_admissions_research_runs_research_run_id",
                        column: x => x.research_run_id,
                        principalTable: "research_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_research_admissions_created_at",
                table: "research_admissions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_research_admissions_owner_subject_id_created_at",
                table: "research_admissions",
                columns: new[] { "owner_subject_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_research_admissions_research_run_id",
                table: "research_admissions",
                column: "research_run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_admissions");
        }
    }
}
