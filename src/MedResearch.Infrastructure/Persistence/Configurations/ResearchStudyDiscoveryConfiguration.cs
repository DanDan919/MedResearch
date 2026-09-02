using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class ResearchStudyDiscoveryConfiguration : IEntityTypeConfiguration<ResearchStudyDiscovery>
{
    public void Configure(EntityTypeBuilder<ResearchStudyDiscovery> builder)
    {
        builder.ToTable("research_study_discoveries");

        builder.HasKey(discovery => discovery.Id);

        builder.Property(discovery => discovery.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(discovery => discovery.ResearchRunId)
            .HasColumnName("research_run_id")
            .IsRequired();

        builder.Property(discovery => discovery.LiteratureSearchId)
            .HasColumnName("literature_search_id")
            .IsRequired();

        builder.Property(discovery => discovery.StudyId)
            .HasColumnName("study_id")
            .IsRequired();

        builder.Property(discovery => discovery.Source)
            .HasColumnName("source")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(discovery => discovery.SourceStudyIdentifier)
            .HasColumnName("source_study_identifier")
            .HasMaxLength(128);

        builder.Property(discovery => discovery.DiscoveredAt)
            .HasColumnName("discovered_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ResearchRun>()
            .WithMany()
            .HasForeignKey(discovery => discovery.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LiteratureSearch>()
            .WithMany()
            .HasForeignKey(discovery => discovery.LiteratureSearchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Study>()
            .WithMany()
            .HasForeignKey(discovery => discovery.StudyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(discovery => new { discovery.ResearchRunId, discovery.StudyId })
            .HasDatabaseName("ix_research_study_discoveries_research_run_id_study_id");

        builder.HasIndex(discovery => new { discovery.LiteratureSearchId, discovery.StudyId })
            .HasDatabaseName("ux_research_study_discoveries_literature_search_id_study_id")
            .IsUnique();

        builder.HasIndex(discovery => discovery.LiteratureSearchId)
            .HasDatabaseName("ix_research_study_discoveries_literature_search_id");

        builder.HasIndex(discovery => discovery.StudyId)
            .HasDatabaseName("ix_research_study_discoveries_study_id");
    }
}
