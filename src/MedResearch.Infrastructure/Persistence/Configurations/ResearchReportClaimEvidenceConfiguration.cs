using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class ResearchReportClaimEvidenceConfiguration : IEntityTypeConfiguration<ResearchReportClaimEvidence>
{
    public void Configure(EntityTypeBuilder<ResearchReportClaimEvidence> builder)
    {
        builder.ToTable("research_report_claim_evidence");

        builder.HasKey(link => new { link.ResearchReportClaimId, link.EvidenceId });

        builder.Property(link => link.ResearchReportClaimId)
            .HasColumnName("research_report_claim_id")
            .IsRequired();

        builder.Property(link => link.EvidenceId)
            .HasColumnName("evidence_id")
            .IsRequired();

        builder.Property(link => link.Ordinal)
            .HasColumnName("ordinal")
            .IsRequired();

        builder.HasOne<ResearchReportClaim>()
            .WithMany()
            .HasForeignKey(link => link.ResearchReportClaimId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Evidence>()
            .WithMany()
            .HasForeignKey(link => link.EvidenceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => link.EvidenceId)
            .HasDatabaseName("ix_research_report_claim_evidence_evidence_id");

        builder.HasIndex(link => new { link.ResearchReportClaimId, link.Ordinal })
            .HasDatabaseName("ux_research_report_claim_evidence_claim_id_ordinal")
            .IsUnique();
    }
}