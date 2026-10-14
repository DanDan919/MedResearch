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
            table.HasCheckConstraint("ck_quick_creation", "(creation_key IS NULL AND creation_fingerprint IS NULL) OR (creation_key IS NOT NULL AND creation_key <> '00000000-0000-0000-0000-000000000000'::uuid AND creation_fingerprint IS NOT NULL AND creation_fingerprint ~ '^[0-9A-F]{64}$')");
            table.HasCheckConstraint("ck_quick_status", "status IN ('Queued','Processing','Completed','Failed')");
            table.HasCheckConstraint("ck_quick_result", "(status = 'Completed' AND ((result IS NOT NULL AND retrieval IS NULL AND COALESCE(result ->> 'QuickRunId' = id::text AND result ->> 'IsSynthetic' = 'true', FALSE)) OR (result IS NULL AND retrieval IS NOT NULL AND COALESCE(retrieval ->> 'IsFinal' = 'true', FALSE)))) OR (status <> 'Completed' AND result IS NULL)");
            table.HasCheckConstraint("ck_quick_retrieval", "retrieval IS NULL OR (status <> 'Queued' AND result IS NULL AND COALESCE(retrieval ->> 'QuickRunId' = id::text, FALSE))");
            table.HasCheckConstraint("ck_quick_answer", "answer_generation IS NULL OR (result IS NULL AND COALESCE(answer_generation ->> 'QuickRunId' = id::text AND retrieval ->> 'IsFinal' = 'true' AND answer_generation ->> 'AttemptCount' IN ('0','1') AND ((answer_generation ->> 'Outcome' = '0' AND status = 'Processing' AND answer_generation ->> 'AttemptCount' = '1' AND answer_generation ->> 'FinishedAt' IS NULL AND answer_generation ->> 'Answer' IS NULL) OR (answer_generation ->> 'Outcome' IN ('1','2') AND status = 'Completed' AND answer_generation ->> 'FinishedAt' IS NOT NULL AND answer_generation -> 'Answer' ->> 'QuickRunId' = id::text) OR (answer_generation ->> 'Outcome' IN ('3','4') AND status = 'Failed' AND answer_generation ->> 'FinishedAt' IS NOT NULL AND answer_generation ->> 'Answer' IS NULL)), FALSE))");
            table.HasCheckConstraint("ck_quick_lease", "lease_version >= 0 AND ((status = 'Processing' AND lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL) OR (status <> 'Processing' AND lease_owner IS NULL AND lease_expires_at IS NULL))");
            table.HasCheckConstraint("ck_quick_terminal", "((status IN ('Completed','Failed')) = (completed_at IS NOT NULL)) AND ((status = 'Failed') = (safe_error IS NOT NULL))");
        });
        builder.HasKey(x => x.Id);
        // Nullable only for historical rows; API creation always supplies both values.
        builder.Property<Guid?>("CreationKey").HasColumnName("creation_key");
        builder.Property<string?>("CreationFingerprint").HasColumnName("creation_fingerprint").HasMaxLength(64);
        builder.HasIndex(nameof(QuickResearchRun.OwnerSubjectId), "CreationKey").IsUnique()
            .HasDatabaseName("ux_quick_owner_creation_key").HasFilter("creation_key IS NOT NULL");
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
        builder.Property(x => x.Retrieval).HasColumnName("retrieval").HasColumnType("jsonb")
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), json => JsonSerializer.Deserialize<QuickRetrievalSnapshot>(json, (JsonSerializerOptions?)null));
        builder.Property(x => x.AnswerGeneration).HasColumnName("answer_generation").HasColumnType("jsonb")
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), json => JsonSerializer.Deserialize<QuickAnswerGeneration>(json, (JsonSerializerOptions?)null));
        builder.HasIndex(x => new { x.OwnerSubjectId, x.CreatedAt, x.Id });
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresAt, x.CreatedAt });
    }
}
