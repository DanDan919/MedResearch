using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedResearch.Infrastructure.Persistence.Configurations;

internal sealed class LiteratureProviderAttemptConfiguration : IEntityTypeConfiguration<LiteratureProviderAttempt>
{
    public void Configure(EntityTypeBuilder<LiteratureProviderAttempt> builder)
    {
        builder.ToTable("literature_provider_attempts", table => table.HasCheckConstraint("ck_provider_attempt_outcome", """
            (status = 'Started' AND completed_at IS NULL AND result_count IS NULL AND failure_category IS NULL AND literature_search_id IS NULL)
            OR (status IN ('SucceededWithResults', 'SucceededZeroResults') AND completed_at IS NOT NULL AND completed_at >= started_at AND result_count IS NOT NULL AND literature_search_id IS NOT NULL AND failure_category IS NULL
                AND ((status = 'SucceededZeroResults' AND result_count = 0) OR (status = 'SucceededWithResults' AND result_count > 0)))
            OR (status IN ('Failed', 'TimedOut', 'Cancelled') AND completed_at IS NOT NULL AND completed_at >= started_at AND result_count IS NULL AND literature_search_id IS NULL AND failure_category IS NOT NULL)
            """));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ResearchRunId).HasColumnName("research_run_id");
        builder.Property(x => x.ResearchPlanId).HasColumnName("research_plan_id");
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Query).HasColumnName("query").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsConcurrencyToken();
        builder.Property(x => x.FailureCategory).HasColumnName("failure_category").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.ResultCount).HasColumnName("result_count");
        builder.Property(x => x.LiteratureSearchId).HasColumnName("literature_search_id");
        builder.HasOne<ResearchRun>().WithMany().HasForeignKey(x => x.ResearchRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ResearchPlan>().WithMany().HasForeignKey(x => x.ResearchPlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiteratureSearch>().WithMany().HasForeignKey(x => x.LiteratureSearchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ResearchRunId, x.StartedAt });
        builder.HasIndex(x => x.LiteratureSearchId).IsUnique().HasFilter("literature_search_id IS NOT NULL");
    }
}
