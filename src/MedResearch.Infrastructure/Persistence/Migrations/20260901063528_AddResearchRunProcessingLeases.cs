using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedResearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchRunProcessingLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_heartbeat_at",
                table: "research_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processing_lease_acquired_at",
                table: "research_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processing_lease_expires_at",
                table: "research_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processing_lease_owner",
                table: "research_runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "processing_lease_version",
                table: "research_runs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_research_runs_status_lease_expires_at_created_at",
                table: "research_runs",
                columns: new[] { "status", "processing_lease_expires_at", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_research_runs_status_lease_expires_at_created_at",
                table: "research_runs");

            migrationBuilder.DropColumn(
                name: "last_heartbeat_at",
                table: "research_runs");

            migrationBuilder.DropColumn(
                name: "processing_lease_acquired_at",
                table: "research_runs");

            migrationBuilder.DropColumn(
                name: "processing_lease_expires_at",
                table: "research_runs");

            migrationBuilder.DropColumn(
                name: "processing_lease_owner",
                table: "research_runs");

            migrationBuilder.DropColumn(
                name: "processing_lease_version",
                table: "research_runs");
        }
    }
}
