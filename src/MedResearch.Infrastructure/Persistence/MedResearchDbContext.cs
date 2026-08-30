using MedResearch.Domain;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Persistence;

public sealed class MedResearchDbContext : DbContext
{
    public MedResearchDbContext(DbContextOptions<MedResearchDbContext> options)
        : base(options)
    {
    }

    public DbSet<ResearchQuestion> ResearchQuestions => Set<ResearchQuestion>();

    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();

    public DbSet<ResearchPlan> ResearchPlans => Set<ResearchPlan>();

    public DbSet<Study> Studies => Set<Study>();

    public DbSet<Evidence> Evidence => Set<Evidence>();

    public DbSet<LiteratureSearch> LiteratureSearches => Set<LiteratureSearch>();

    public DbSet<ResearchStudyDiscovery> ResearchStudyDiscoveries => Set<ResearchStudyDiscovery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedResearchDbContext).Assembly);
    }
}
