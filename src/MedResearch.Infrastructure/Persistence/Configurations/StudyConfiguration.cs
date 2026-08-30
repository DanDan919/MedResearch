using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class StudyConfiguration : IEntityTypeConfiguration<Study>
{
    public void Configure(EntityTypeBuilder<Study> builder)
    {
        builder.ToTable("studies");

        builder.HasKey(study => study.Id);

        builder.Property(study => study.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(study => study.Title)
            .HasColumnName("title")
            .HasMaxLength(1_000)
            .IsRequired();

        builder.Property(study => study.Abstract)
            .HasColumnName("abstract");

        builder.Property(study => study.Doi)
            .HasColumnName("doi")
            .HasMaxLength(255);

        builder.Property(study => study.Pmid)
            .HasColumnName("pmid")
            .HasMaxLength(64);

        builder.Property(study => study.Journal)
            .HasColumnName("journal")
            .HasMaxLength(512);

        builder.Property(study => study.PublicationDate)
            .HasColumnName("publication_date")
            .HasColumnType("date");

        builder.Property(study => study.Source)
            .HasColumnName("source")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(study => study.Doi)
            .HasDatabaseName("ix_studies_doi")
            .HasFilter("doi IS NOT NULL");

        builder.HasIndex(study => study.Pmid)
            .HasDatabaseName("ix_studies_pmid")
            .HasFilter("pmid IS NOT NULL");
    }
}
