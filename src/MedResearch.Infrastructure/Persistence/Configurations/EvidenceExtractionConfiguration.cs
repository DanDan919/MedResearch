using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class EvidenceExtractionConfiguration : IEntityTypeConfiguration<EvidenceExtraction>
{
    public void Configure(EntityTypeBuilder<EvidenceExtraction> builder)
    {
        builder.ToTable("evidence_extractions");

        builder.HasKey(extraction => extraction.Id);

        builder.Property(extraction => extraction.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(extraction => extraction.ResearchRunId)
            .HasColumnName("research_run_id")
            .IsRequired();

        builder.Property(extraction => extraction.StudyId)
            .HasColumnName("study_id")
            .IsRequired();

        builder.Property(extraction => extraction.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(extraction => extraction.SkipReason)
            .HasColumnName("skip_reason")
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.Property(extraction => extraction.SourceScope)
            .HasColumnName("source_scope")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(extraction => extraction.Provider)
            .HasColumnName("provider")
            .HasMaxLength(64);

        builder.Property(extraction => extraction.Model)
            .HasColumnName("model")
            .HasMaxLength(128);

        builder.Property(extraction => extraction.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(extraction => extraction.ExtractedAt)
            .HasColumnName("extracted_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(extraction => extraction.EvidenceCount)
            .HasColumnName("evidence_count")
            .IsRequired();

        builder.Property(extraction => extraction.GroundingValidated)
            .HasColumnName("grounding_validated")
            .IsRequired();

        builder.HasOne<ResearchRun>()
            .WithMany()
            .HasForeignKey(extraction => extraction.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Study>()
            .WithMany()
            .HasForeignKey(extraction => extraction.StudyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(extraction => new { extraction.ResearchRunId, extraction.StudyId, extraction.PromptVersion })
            .HasDatabaseName("ux_evidence_extractions_research_run_id_study_id_prompt_version")
            .IsUnique();

        builder.HasIndex(extraction => new { extraction.ResearchRunId, extraction.Status })
            .HasDatabaseName("ix_evidence_extractions_research_run_id_status");

        builder.HasIndex(extraction => extraction.StudyId)
            .HasDatabaseName("ix_evidence_extractions_study_id");
    }
}
