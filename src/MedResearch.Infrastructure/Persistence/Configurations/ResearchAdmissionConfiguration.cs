using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

public sealed class ResearchAdmissionConfiguration : IEntityTypeConfiguration<ResearchAdmissionEntity>
{
    public void Configure(EntityTypeBuilder<ResearchAdmissionEntity> builder)
    {
        builder.ToTable("research_admissions");
        builder.HasKey(admission => new { admission.OwnerSubjectId, admission.IdempotencyKey });
        builder.Property(admission => admission.OwnerSubjectId).HasColumnName("owner_subject_id").HasMaxLength(200).IsRequired();
        builder.Property(admission => admission.IdempotencyKey).HasColumnName("idempotency_key");
        builder.Property(admission => admission.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(admission => admission.ResearchRunId).HasColumnName("research_run_id");
        builder.Property(admission => admission.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(admission => admission.ResearchRunId).IsUnique();
        builder.HasIndex(admission => admission.CreatedAt);
        builder.HasIndex(admission => new { admission.OwnerSubjectId, admission.CreatedAt });
        builder.HasOne<ResearchRun>().WithOne().HasForeignKey<ResearchAdmissionEntity>(admission => admission.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
