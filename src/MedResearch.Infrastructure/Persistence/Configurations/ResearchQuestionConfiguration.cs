using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class ResearchQuestionConfiguration : IEntityTypeConfiguration<ResearchQuestion>
{
    public void Configure(EntityTypeBuilder<ResearchQuestion> builder)
    {
        builder.ToTable("research_questions");

        builder.HasKey(question => question.Id);

        builder.Property(question => question.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(question => question.Text)
            .HasColumnName("text")
            .HasMaxLength(1_000)
            .IsRequired();

        builder.Property(question => question.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
