using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class ResearchPlanConfiguration : IEntityTypeConfiguration<ResearchPlan>
{
    public void Configure(EntityTypeBuilder<ResearchPlan> builder)
    {
        builder.ToTable("research_plans");

        builder.HasKey(plan => plan.Id);

        builder.Property(plan => plan.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(plan => plan.ResearchRunId)
            .HasColumnName("research_run_id")
            .IsRequired();

        builder.Property(plan => plan.ResearchQuestionId)
            .HasColumnName("research_question_id")
            .IsRequired();

        builder.Property(plan => plan.OriginalQuestion)
            .HasColumnName("original_question")
            .HasMaxLength(2_000)
            .IsRequired();

        builder.Property(plan => plan.Population)
            .HasColumnName("population")
            .HasMaxLength(500);

        builder.Property(plan => plan.ExposureOrIntervention)
            .HasColumnName("exposure_or_intervention")
            .HasMaxLength(500);

        builder.Property(plan => plan.Comparator)
            .HasColumnName("comparator")
            .HasMaxLength(500);

        builder.Property(plan => plan.Outcomes)
            .HasColumnName("outcomes")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(plan => plan.PreferredStudyTypes)
            .HasColumnName("preferred_study_types")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(plan => plan.SearchQueries)
            .HasColumnName("search_queries")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(plan => plan.ExclusionHints)
            .HasColumnName("exclusion_hints")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(plan => plan.Provider)
            .HasColumnName("provider")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(plan => plan.Model)
            .HasColumnName("model")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(plan => plan.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(plan => plan.GeneratedAt)
            .HasColumnName("generated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ResearchRun>()
            .WithMany()
            .HasForeignKey(plan => plan.ResearchRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ResearchQuestion>()
            .WithMany()
            .HasForeignKey(plan => plan.ResearchQuestionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(plan => plan.ResearchRunId)
            .HasDatabaseName("ux_research_plans_research_run_id")
            .IsUnique();

        builder.HasIndex(plan => plan.ResearchQuestionId)
            .HasDatabaseName("ix_research_plans_research_question_id");
    }
}
