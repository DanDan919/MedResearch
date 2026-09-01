using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class ResearchReportClaimConfiguration : IEntityTypeConfiguration<ResearchReportClaim>
{
    public void Configure(EntityTypeBuilder<ResearchReportClaim> builder)
    {
        builder.ToTable("research_report_claims");

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
            .HasMaxLength(800)
            .IsRequired();

        builder.Property(claim => claim.Ordinal)
            .HasColumnName("ordinal")
            .IsRequired();

        builder.HasOne<ResearchReport>()
            .WithMany()
            .HasForeignKey(claim => claim.ResearchReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(claim => new { claim.ResearchReportId, claim.Ordinal })
            .HasDatabaseName("ux_research_report_claims_research_report_id_ordinal")
            .IsUnique();
    }
}