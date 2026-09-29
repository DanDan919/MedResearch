using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class QuantitativeSynthesisArtifactConfiguration : IEntityTypeConfiguration<QuantitativeSynthesisArtifactEntity>
{
    public void Configure(EntityTypeBuilder<QuantitativeSynthesisArtifactEntity> builder)
    {
        builder.ToTable("quantitative_synthesis_artifacts");
        builder.HasKey(artifact => artifact.Id);

        builder.Property(artifact => artifact.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(artifact => artifact.ResearchRunId).HasColumnName("research_run_id").IsRequired();
        builder.Property(artifact => artifact.GroupKey).HasColumnName("group_key").HasMaxLength(512).IsRequired();
        builder.Property(artifact => artifact.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(artifact => artifact.AlgorithmVersion).HasColumnName("algorithm_version").HasMaxLength(128).IsRequired();
        builder.Property(artifact => artifact.OutputConfidenceLevel).HasColumnName("output_confidence_level").HasColumnType("numeric(8,7)").IsRequired();
        builder.Property(artifact => artifact.EvidenceCount).HasColumnName("evidence_count").IsRequired();
        builder.Property(artifact => artifact.UniqueStudyCount).HasColumnName("unique_study_count").IsRequired();
        builder.Property(artifact => artifact.SnapshotFingerprint).HasColumnName("snapshot_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(artifact => artifact.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb").IsRequired();
        builder.Property(artifact => artifact.PersistedAt).HasColumnName("persisted_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<MedResearch.Domain.ResearchRun>()
            .WithMany()
            .HasForeignKey(artifact => artifact.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(artifact => new { artifact.ResearchRunId, artifact.GroupKey })
            .HasDatabaseName("ux_quantitative_synthesis_artifacts_run_group")
            .IsUnique();
        builder.HasIndex(artifact => new { artifact.ResearchRunId, artifact.Status })
            .HasDatabaseName("ix_quantitative_synthesis_artifacts_run_status");
    }
}

internal sealed class QuantitativeSynthesisContributionSnapshotConfiguration : IEntityTypeConfiguration<QuantitativeSynthesisContributionSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<QuantitativeSynthesisContributionSnapshotEntity> builder)
    {
        builder.ToTable("quantitative_synthesis_contribution_snapshots");
        builder.HasKey(snapshot => new { snapshot.ArtifactId, snapshot.AnalysisMethod, snapshot.Ordinal });

        builder.Property(snapshot => snapshot.ArtifactId).HasColumnName("artifact_id").IsRequired();
        builder.Property(snapshot => snapshot.AnalysisMethod).HasColumnName("analysis_method").HasMaxLength(64).IsRequired();
        builder.Property(snapshot => snapshot.Ordinal).HasColumnName("ordinal").IsRequired();
        builder.Property(snapshot => snapshot.EvidenceId).HasColumnName("evidence_id").IsRequired();
        builder.Property(snapshot => snapshot.StudyId).HasColumnName("study_id").IsRequired();
        builder.Property(snapshot => snapshot.EvidenceExtractionId).HasColumnName("evidence_extraction_id").IsRequired();
        builder.Property(snapshot => snapshot.SourceMaterialId).HasColumnName("source_material_id").IsRequired();
        builder.Property(snapshot => snapshot.AnalysisScaleEffect).HasColumnName("analysis_scale_effect").HasColumnType("double precision").IsRequired();
        builder.Property(snapshot => snapshot.AnalysisScaleVariance).HasColumnName("analysis_scale_variance").HasColumnType("double precision").IsRequired();
        builder.Property(snapshot => snapshot.AnalysisScaleStandardError).HasColumnName("analysis_scale_standard_error").HasColumnType("double precision").IsRequired();
        builder.Property(snapshot => snapshot.Weight).HasColumnName("weight").HasColumnType("double precision").IsRequired();
        builder.Property(snapshot => snapshot.NormalizedWeight).HasColumnName("normalized_weight").HasColumnType("double precision").IsRequired();

        builder.HasOne<QuantitativeSynthesisArtifactEntity>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.ArtifactId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MedResearch.Domain.Evidence>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MedResearch.Domain.Study>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.StudyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MedResearch.Domain.EvidenceExtraction>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.EvidenceExtractionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MedResearch.Domain.SourceMaterial>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.SourceMaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(snapshot => snapshot.EvidenceId)
            .HasDatabaseName("ix_quantitative_synthesis_contribution_snapshots_evidence_id");
    }
}
