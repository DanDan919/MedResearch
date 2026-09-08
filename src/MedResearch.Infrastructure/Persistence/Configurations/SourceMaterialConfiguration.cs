using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class SourceMaterialConfiguration : IEntityTypeConfiguration<SourceMaterial>
{
    public void Configure(EntityTypeBuilder<SourceMaterial> builder)
    {
        builder.ToTable("source_materials");

        builder.HasKey(material => material.Id);

        builder.Property(material => material.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(material => material.StudyId)
            .HasColumnName("study_id")
            .IsRequired();

        builder.Property(material => material.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(material => material.Provider)
            .HasColumnName("provider")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(material => material.ProviderSourceId)
            .HasColumnName("provider_source_id")
            .HasMaxLength(128);

        builder.Property(material => material.RetrievalMethod)
            .HasColumnName("retrieval_method")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(material => material.Content)
            .HasColumnName("content")
            .IsRequired();

        builder.Property(material => material.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(material => material.ContentVersion)
            .HasColumnName("content_version")
            .IsRequired();

        builder.Property(material => material.RetrievedAt)
            .HasColumnName("retrieved_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(material => material.SourceUpdatedAt)
            .HasColumnName("source_updated_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(material => material.License)
            .HasColumnName("license")
            .HasMaxLength(256);

        builder.Property(material => material.LicenseUrl)
            .HasColumnName("license_url")
            .HasMaxLength(512);

        builder.Property(material => material.AccessStatus)
            .HasColumnName("access_status")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(material => material.CharacterCount)
            .HasColumnName("character_count")
            .IsRequired();

        builder.Property(material => material.WasTruncated)
            .HasColumnName("was_truncated")
            .IsRequired();

        builder.Property(material => material.IsCurrent)
            .HasColumnName("is_current")
            .IsRequired();

        builder.Property(material => material.SectionNames)
            .HasColumnName("section_names")
            .HasColumnType("text[]")
            .IsRequired();

        builder.HasOne<Study>()
            .WithMany()
            .HasForeignKey(material => material.StudyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(material => material.StudyId)
            .HasDatabaseName("ix_source_materials_study_id");

        builder.HasIndex(material => new { material.StudyId, material.Type, material.Provider, material.ProviderSourceId, material.ContentHash })
            .HasDatabaseName("ux_source_materials_identity_hash")
            .IsUnique();

        builder.HasIndex(material => new { material.StudyId, material.Type, material.IsCurrent })
            .HasDatabaseName("ix_source_materials_study_id_type_is_current");
    }
}
