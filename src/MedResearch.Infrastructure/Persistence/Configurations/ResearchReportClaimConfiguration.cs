using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class ResearchReportClaimConfiguration : IEntityTypeConfiguration<ResearchReportClaim>
{
    public void Configure(EntityTypeBuilder<ResearchReportClaim> builder)
    {
        builder.ToTable("research_report_claims", table => table.HasCheckConstraint("ck_report_claim_structured_authority", """
            (grounding_status = 'LegacyUnverified' AND semantics IS NULL AND semantic_key IS NULL AND quantitative_artifact_id IS NULL AND numeric_evidence_id IS NULL)
            OR (grounding_status = 'StructuredValidated' AND semantics IS NOT NULL AND jsonb_typeof(semantics) = 'object'
                AND (semantics->>'ProtocolVersion') IS NOT DISTINCT FROM 'structured-claim-v1' AND semantic_key IS NOT NULL AND length(semantic_key) = 64
                AND (semantics->>'QuantitativeArtifactId') IS NOT DISTINCT FROM quantitative_artifact_id::text
                AND (semantics->>'NumericEvidenceId') IS NOT DISTINCT FROM numeric_evidence_id::text)
            """));

        builder.HasKey(claim => claim.Id);

        builder.Property(claim => claim.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(claim => claim.ResearchReportId)
            .HasColumnName("research_report_id")
            .IsRequired();

        builder.Property(claim => claim.ClaimType)
            .HasColumnName("claim_type")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(claim => claim.Direction)
            .HasColumnName("direction")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(claim => claim.Text)
            .HasColumnName("text")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(claim => claim.Ordinal)
            .HasColumnName("ordinal")
            .IsRequired();

        builder.Property(claim => claim.GroundingStatus).HasColumnName("grounding_status").HasConversion<string>().HasMaxLength(32)
            .HasDefaultValue(ResearchClaimGroundingStatus.LegacyUnverified);
        builder.Property(claim => claim.Semantics).HasColumnName("semantics").HasColumnType("jsonb")
            .HasConversion(value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), value => JsonSerializer.Deserialize<ResearchClaimSemantics>(value, (JsonSerializerOptions?)null));
        builder.Property(claim => claim.SemanticKey).HasColumnName("semantic_key").HasMaxLength(64);
        builder.Property(claim => claim.QuantitativeArtifactId).HasColumnName("quantitative_artifact_id");
        builder.Property(claim => claim.NumericEvidenceId).HasColumnName("numeric_evidence_id");
        builder.HasOne<QuantitativeSynthesisArtifactEntity>().WithMany().HasForeignKey(claim => claim.QuantitativeArtifactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Evidence>().WithMany().HasForeignKey(claim => claim.NumericEvidenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(claim => new { claim.ResearchReportId, claim.SemanticKey }).IsUnique().HasFilter("semantic_key IS NOT NULL");

        builder.HasOne<ResearchReport>()
            .WithMany()
            .HasForeignKey(claim => claim.ResearchReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(claim => new { claim.ResearchReportId, claim.Ordinal })
            .HasDatabaseName("ux_research_report_claims_research_report_id_ordinal")
            .IsUnique();
    }
}
