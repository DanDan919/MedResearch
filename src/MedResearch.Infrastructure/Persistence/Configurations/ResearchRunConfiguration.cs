using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class ResearchRunConfiguration : IEntityTypeConfiguration<ResearchRun>
{
    public void Configure(EntityTypeBuilder<ResearchRun> builder)
    {
        builder.ToTable("research_runs");

        builder.HasKey(run => run.Id);

        builder.Property(run => run.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(run => run.ResearchQuestionId)
            .HasColumnName("research_question_id")
            .IsRequired();

        builder.Property(run => run.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(run => run.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(run => run.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(run => run.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(run => run.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(2_000);

        builder.Property(run => run.ProcessingLeaseOwner)
            .HasColumnName("processing_lease_owner")
            .HasMaxLength(128);

        builder.Property(run => run.ProcessingLeaseAcquiredAt)
            .HasColumnName("processing_lease_acquired_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(run => run.ProcessingLeaseExpiresAt)
            .HasColumnName("processing_lease_expires_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(run => run.LastHeartbeatAt)
            .HasColumnName("last_heartbeat_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(run => run.ProcessingLeaseVersion)
            .HasColumnName("processing_lease_version")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.HasOne<ResearchQuestion>()
            .WithMany()
            .HasForeignKey(run => run.ResearchQuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(run => new { run.Status, run.CreatedAt })
            .HasDatabaseName("ix_research_runs_status_created_at");

        builder.HasIndex(run => new { run.Status, run.ProcessingLeaseExpiresAt, run.CreatedAt })
            .HasDatabaseName("ix_research_runs_status_lease_expires_at_created_at");
    }
}
