using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class EvidenceEvaluationConfiguration : IEntityTypeConfiguration<EvidenceEvaluation>
{
    public void Configure(EntityTypeBuilder<EvidenceEvaluation> builder)
    {
        builder.ToTable("evidence_evaluations");

        builder.HasKey(evaluation => evaluation.Id);

        builder.Property(evaluation => evaluation.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(evaluation => evaluation.ResearchRunId)
            .HasColumnName("research_run_id")
            .IsRequired();

        builder.Property(evaluation => evaluation.StudyId)
            .HasColumnName("study_id")
            .IsRequired();

        builder.Property(evaluation => evaluation.EvidenceIds)
            .HasColumnName("evidence_ids")
            .HasColumnType("uuid[]")
            .IsRequired();

        builder.Property(evaluation => evaluation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(evaluation => evaluation.SkipReason)
            .HasColumnName("skip_reason")
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.Property(evaluation => evaluation.SourceScope)
            .HasColumnName("source_scope")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(evaluation => evaluation.EvaluatorProvider)
            .HasColumnName("evaluator_provider")
            .HasMaxLength(64);

        builder.Property(evaluation => evaluation.EvaluatorModel)
            .HasColumnName("evaluator_model")
            .HasMaxLength(128);

        builder.Property(evaluation => evaluation.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(evaluation => evaluation.EvaluatedAt)
            .HasColumnName("evaluated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(evaluation => evaluation.StudyDesign)
            .HasColumnName("study_design")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.SampleInformation)
            .HasColumnName("sample_information")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.ComparatorPresence)
            .HasColumnName("comparator_presence")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.ComparatorDescription)
            .HasColumnName("comparator_description")
            .HasMaxLength(300);

        builder.Property(evaluation => evaluation.Randomization)
            .HasColumnName("randomization")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.Blinding)
            .HasColumnName("blinding")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.AllocationConcealment)
            .HasColumnName("allocation_concealment")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.AttritionMissingData)
            .HasColumnName("attrition_missing_data")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.Precision)
            .HasColumnName("precision")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.Directness)
            .HasColumnName("directness")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.OverallConfidence)
            .HasColumnName("overall_confidence")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(evaluation => evaluation.Rationale)
            .HasColumnName("rationale")
            .HasMaxLength(1_000)
            .IsRequired();

        builder.Property(evaluation => evaluation.ReportingLimitations)
            .HasColumnName("reporting_limitations")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(evaluation => evaluation.AuthorReportedLimitations)
            .HasColumnName("author_reported_limitations")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(evaluation => evaluation.HasSampleSize)
            .HasColumnName("has_sample_size")
            .IsRequired();

        builder.Property(evaluation => evaluation.HasEffectEstimate)
            .HasColumnName("has_effect_estimate")
            .IsRequired();

        builder.Property(evaluation => evaluation.HasConfidenceInterval)
            .HasColumnName("has_confidence_interval")
            .IsRequired();

        builder.Property(evaluation => evaluation.HasPValue)
            .HasColumnName("has_p_value")
            .IsRequired();

        builder.Property(evaluation => evaluation.HasComparator)
            .HasColumnName("has_comparator")
            .IsRequired();

        builder.Property(evaluation => evaluation.UnknownDomainCount)
            .HasColumnName("unknown_domain_count")
            .IsRequired();

        builder.Property(evaluation => evaluation.InsufficientSourceDomainCount)
            .HasColumnName("insufficient_source_domain_count")
            .IsRequired();

        builder.HasOne<ResearchRun>()
            .WithMany()
            .HasForeignKey(evaluation => evaluation.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Study>()
            .WithMany()
            .HasForeignKey(evaluation => evaluation.StudyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(evaluation => new { evaluation.ResearchRunId, evaluation.StudyId, evaluation.PromptVersion })
            .HasDatabaseName("ux_evidence_evaluations_research_run_id_study_id_prompt_version")
            .IsUnique();

        builder.HasIndex(evaluation => new { evaluation.ResearchRunId, evaluation.Status })
            .HasDatabaseName("ix_evidence_evaluations_research_run_id_status");

        builder.HasIndex(evaluation => evaluation.StudyId)
            .HasDatabaseName("ix_evidence_evaluations_study_id");
    }
}
