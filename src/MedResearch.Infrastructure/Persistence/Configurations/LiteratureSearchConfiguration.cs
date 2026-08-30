using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class LiteratureSearchConfiguration : IEntityTypeConfiguration<LiteratureSearch>
{
    public void Configure(EntityTypeBuilder<LiteratureSearch> builder)
    {
        builder.ToTable("literature_searches");

        builder.HasKey(search => search.Id);

        builder.Property(search => search.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(search => search.ResearchRunId)
            .HasColumnName("research_run_id")
            .IsRequired();

        builder.Property(search => search.Source)
            .HasColumnName("source")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(search => search.Query)
            .HasColumnName("query")
            .HasMaxLength(2_000)
            .IsRequired();

        builder.Property(search => search.SearchedAt)
            .HasColumnName("searched_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(search => search.ResultCount)
            .HasColumnName("result_count")
            .IsRequired();

        builder.Property(search => search.PersistedStudyCount)
            .HasColumnName("persisted_study_count")
            .IsRequired();

        builder.Property(search => search.DuplicateStudyCount)
            .HasColumnName("duplicate_study_count")
            .IsRequired();

        builder.HasOne<ResearchRun>()
            .WithMany()
            .HasForeignKey(search => search.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(search => new { search.ResearchRunId, search.SearchedAt })
            .HasDatabaseName("ix_literature_searches_research_run_id_searched_at");
    }
}
