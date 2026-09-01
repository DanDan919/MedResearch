using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class ResearchReportConfiguration : IEntityTypeConfiguration<ResearchReport>
{
    public void Configure(EntityTypeBuilder<ResearchReport> builder)
    {
        builder.ToTable("research_reports");

        builder.HasKey(report => report.Id);

        builder.Property(report => report.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(report => report.ResearchRunId)
            .HasColumnName("research_run_id")
            .IsRequired();

        builder.Property(report => report.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(report => report.InsufficientEvidenceReason)
            .HasColumnName("insufficient_evidence_reason")
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.Property(report => report.ExecutiveSummary)
            .HasColumnName("executive_summary")
            .HasMaxLength(2_500)
            .IsRequired();

        builder.Property(report => report.EvidenceSummary)
            .HasColumnName("evidence_summary")
            .HasMaxLength(2_500)
            .IsRequired();

        builder.Property(report => report.ConflictSummary)
            .HasColumnName("conflict_summary")
            .HasMaxLength(2_500)
            .IsRequired();

        builder.Property(report => report.LimitationsSummary)
            .HasColumnName("limitations_summary")
            .HasMaxLength(2_500)
            .IsRequired();

        builder.Property(report => report.Conclusion)
            .HasColumnName("conclusion")
            .HasMaxLength(2_500)
            .IsRequired();

        builder.Property(report => report.SynthesisConfidence)
            .HasColumnName("synthesis_confidence")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(report => report.SynthesizerProvider)
            .HasColumnName("synthesizer_provider")
            .HasMaxLength(64);

        builder.Property(report => report.SynthesizerModel)
            .HasColumnName("synthesizer_model")
            .HasMaxLength(128);

        builder.Property(report => report.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(report => report.GeneratedAt)
            .HasColumnName("generated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(report => report.DiscoveredStudyCount).HasColumnName("discovered_study_count").IsRequired();
        builder.Property(report => report.ExtractedStudyCount).HasColumnName("extracted_study_count").IsRequired();
        builder.Property(report => report.EvaluatedStudyCount).HasColumnName("evaluated_study_count").IsRequired();
        builder.Property(report => report.EvidenceFindingCount).HasColumnName("evidence_finding_count").IsRequired();
        builder.Property(report => report.IncludedStudyCount).HasColumnName("included_study_count").IsRequired();
        builder.Property(report => report.IncludedEvidenceFindingCount).HasColumnName("included_evidence_finding_count").IsRequired();
        builder.Property(report => report.ClaimCount).HasColumnName("claim_count").IsRequired();
        builder.Property(report => report.SearchQueryCount).HasColumnName("search_query_count").IsRequired();
        builder.Property(report => report.StudiesWithNoExtractableEvidence).HasColumnName("studies_with_no_extractable_evidence").IsRequired();
        builder.Property(report => report.StudiesWithInsufficientEvaluationSource).HasColumnName("studies_with_insufficient_evaluation_source").IsRequired();
        builder.Property(report => report.PotentialConflictDetected).HasColumnName("potential_conflict_detected").IsRequired();
        builder.Property(report => report.EvidenceTruncated).HasColumnName("evidence_truncated").IsRequired();
        builder.Property(report => report.UsesAbstractLevelEvidenceOnly).HasColumnName("uses_abstract_level_evidence_only").IsRequired();

        builder.Property(report => report.SearchedSources)
            .HasColumnName("searched_sources")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(report => report.DeterministicLimitations)
            .HasColumnName("deterministic_limitations")
            .HasColumnType("text[]")
            .IsRequired();

        builder.HasOne<ResearchRun>()
            .WithMany()
            .HasForeignKey(report => report.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(report => new { report.ResearchRunId, report.PromptVersion })
            .HasDatabaseName("ux_research_reports_research_run_id_prompt_version")
            .IsUnique();

        builder.HasIndex(report => new { report.ResearchRunId, report.Status })
            .HasDatabaseName("ix_research_reports_research_run_id_status");
    }
}