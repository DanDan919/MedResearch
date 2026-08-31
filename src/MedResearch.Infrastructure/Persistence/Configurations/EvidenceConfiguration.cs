using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class EvidenceConfiguration : IEntityTypeConfiguration<Evidence>
{
    public void Configure(EntityTypeBuilder<Evidence> builder)
    {
        builder.ToTable("evidence");

        builder.HasKey(evidence => evidence.Id);

        builder.Property(evidence => evidence.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(evidence => evidence.ResearchRunId)
            .HasColumnName("research_run_id")
            .IsRequired();

        builder.Property(evidence => evidence.StudyId)
            .HasColumnName("study_id")
            .IsRequired();

        builder.Property(evidence => evidence.EvidenceExtractionId)
            .HasColumnName("evidence_extraction_id")
            .IsRequired();

        builder.Property(evidence => evidence.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(evidence => evidence.ResultSummary)
            .HasColumnName("result_summary")
            .HasMaxLength(800)
            .IsRequired();

        builder.Property(evidence => evidence.SupportingText)
            .HasColumnName("supporting_text")
            .HasMaxLength(1_000)
            .IsRequired();

        builder.Property(evidence => evidence.Direction)
            .HasColumnName("direction")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(evidence => evidence.SourceScope)
            .HasColumnName("source_scope")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(evidence => evidence.ExtractedAt)
            .HasColumnName("extracted_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(evidence => evidence.GroundingValidated)
            .HasColumnName("grounding_validated")
            .IsRequired();

        builder.Property(evidence => evidence.Population)
            .HasColumnName("population")
            .HasMaxLength(300);

        builder.Property(evidence => evidence.ExposureOrIntervention)
            .HasColumnName("exposure_or_intervention")
            .HasMaxLength(300);

        builder.Property(evidence => evidence.Comparator)
            .HasColumnName("comparator")
            .HasMaxLength(300);

        builder.Property(evidence => evidence.StudyDesign)
            .HasColumnName("study_design")
            .HasMaxLength(100);

        builder.Property(evidence => evidence.SampleSize)
            .HasColumnName("sample_size");

        builder.Property(evidence => evidence.EffectMeasure)
            .HasColumnName("effect_measure")
            .HasMaxLength(100);

        builder.Property(evidence => evidence.EffectValue)
            .HasColumnName("effect_value")
            .HasColumnType("numeric(18,6)");

        builder.Property(evidence => evidence.ConfidenceIntervalLower)
            .HasColumnName("confidence_interval_lower")
            .HasColumnType("numeric(18,6)");

        builder.Property(evidence => evidence.ConfidenceIntervalUpper)
            .HasColumnName("confidence_interval_upper")
            .HasColumnType("numeric(18,6)");

        builder.Property(evidence => evidence.PValue)
            .HasColumnName("p_value")
            .HasColumnType("numeric(18,6)");

        builder.HasOne<ResearchRun>()
            .WithMany()
            .HasForeignKey(evidence => evidence.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Study>()
            .WithMany()
            .HasForeignKey(evidence => evidence.StudyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EvidenceExtraction>()
            .WithMany()
            .HasForeignKey(evidence => evidence.EvidenceExtractionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(evidence => evidence.ResearchRunId)
            .HasDatabaseName("ix_evidence_research_run_id");

        builder.HasIndex(evidence => evidence.StudyId)
            .HasDatabaseName("ix_evidence_study_id");

        builder.HasIndex(evidence => evidence.EvidenceExtractionId)
            .HasDatabaseName("ix_evidence_evidence_extraction_id");
    }
}
