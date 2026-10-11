using System.Text.Json;
using MedResearch.Domain.QuickResearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

public sealed class QuickResearchRunConfiguration : IEntityTypeConfiguration<QuickResearchRun>
{
    public void Configure(EntityTypeBuilder<QuickResearchRun> builder)
    {
        builder.ToTable("quick_research_runs", table =>
        {
            table.HasCheckConstraint("ck_quick_status", "status IN ('Queued','Processing','Completed','Failed')");
            table.HasCheckConstraint("ck_quick_result", "(status = 'Completed' AND result IS NOT NULL AND COALESCE(result ->> 'QuickRunId' = id::text AND result ->> 'IsSynthetic' = 'true', FALSE)) OR (status <> 'Completed' AND result IS NULL)");
            table.HasCheckConstraint("ck_quick_lease", "lease_version >= 0 AND ((status = 'Processing' AND lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL) OR (status <> 'Processing' AND lease_owner IS NULL AND lease_expires_at IS NULL))");
            table.HasCheckConstraint("ck_quick_terminal", "((status IN ('Completed','Failed')) = (completed_at IS NOT NULL)) AND ((status = 'Failed') = (safe_error IS NOT NULL))");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.OwnerSubjectId).HasColumnName("owner_subject_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Question).HasColumnName("question").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.SafeError).HasColumnName("safe_error").HasMaxLength(64);
        builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(200);
        builder.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(x => x.LeaseVersion).HasColumnName("lease_version");
        builder.Property(x => x.Result).HasColumnName("result").HasColumnType("jsonb")
            .HasConversion(result => JsonSerializer.Serialize(result, (JsonSerializerOptions?)null), json => JsonSerializer.Deserialize<QuickResearchResult>(json, (JsonSerializerOptions?)null));
        builder.HasIndex(x => new { x.OwnerSubjectId, x.CreatedAt, x.Id });
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresAt, x.CreatedAt });
    }
}
