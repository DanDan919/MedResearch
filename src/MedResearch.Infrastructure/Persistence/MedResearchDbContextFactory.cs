using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedResearch.Infrastructure.Persistence;

public sealed class MedResearchDbContextFactory : IDesignTimeDbContextFactory<MedResearchDbContext>
{
    public MedResearchDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MedResearch")
            ?? "Host=localhost;Port=5432;Database=medresearch;Username=medresearch;Password=medresearch_dev_password";

        var optionsBuilder = new DbContextOptionsBuilder<MedResearchDbContext>();
        optionsBuilder.UseNpgsql(connectionString, options =>
            options.MigrationsAssembly(typeof(MedResearchDbContext).Assembly.FullName));

        return new MedResearchDbContext(optionsBuilder.Options);
    }
}
