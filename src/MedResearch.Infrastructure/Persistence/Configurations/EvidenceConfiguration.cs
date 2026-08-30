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

        builder.Property(evidence => evidence.StudyId)
            .HasColumnName("study_id")
            .IsRequired();

        builder.Property(evidence => evidence.Claim)
            .HasColumnName("claim")
            .HasMaxLength(4_000)
            .IsRequired();

        builder.Property(evidence => evidence.Direction)
            .HasColumnName("direction")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(evidence => evidence.Confidence)
            .HasColumnName("confidence")
            .HasColumnType("numeric(5,4)");

        builder.HasOne<Study>()
            .WithMany()
            .HasForeignKey(evidence => evidence.StudyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
