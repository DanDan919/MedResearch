using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

public sealed class SynthesisRetryAttemptConfiguration : IEntityTypeConfiguration<SynthesisRetryAttemptEntity>
{
    public void Configure(EntityTypeBuilder<SynthesisRetryAttemptEntity> builder)
    {
        builder.ToTable("synthesis_retry_attempts", table =>
        {
            table.HasCheckConstraint("ck_synthesis_retry_budget", "generation_budget BETWEEN 1 AND 3 AND generation_calls_reserved BETWEEN 0 AND generation_budget AND (NOT generation_pending OR generation_calls_reserved > 0)");
            table.HasCheckConstraint("ck_synthesis_retry_status", "status IN ('Queued','Running','Succeeded','Failed','OutcomeUnknown')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ResearchRunId).HasColumnName("research_run_id");
        builder.Property(x => x.RequestedByOwner).HasColumnName("requested_by_owner").HasMaxLength(200).IsRequired();
        builder.Property(x => x.IdempotencyKeyHash).HasColumnName("idempotency_key_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InputFingerprint).HasColumnName("input_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestedAt).HasColumnName("requested_at");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.LeaseVersion).HasColumnName("lease_version");
        builder.Property(x => x.GenerationBudget).HasColumnName("generation_budget");
        builder.Property(x => x.GenerationCallsReserved).HasColumnName("generation_calls_reserved");
        builder.Property(x => x.GenerationPending).HasColumnName("generation_pending");
        builder.Property(x => x.FailureCategory).HasColumnName("failure_category").HasMaxLength(64);
        builder.HasIndex(x => x.ResearchRunId).IsUnique();
        builder.HasOne<ResearchRun>().WithOne().HasForeignKey<SynthesisRetryAttemptEntity>(x => x.ResearchRunId).OnDelete(DeleteBehavior.Restrict);
    }
}
