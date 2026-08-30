using MedResearch.Application.Research;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Research;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedResearch.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MedResearch");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'MedResearch' is required.");
        }

        services.AddDbContext<MedResearchDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsAssembly(typeof(MedResearchDbContext).Assembly.FullName)));

        services.AddScoped<IResearchStore, EfResearchStore>();

        services.AddHealthChecks()
            .AddDbContextCheck<MedResearchDbContext>("postgresql", tags: ["database", "postgresql"]);

        if (bool.TryParse(configuration["Database:ApplyMigrationsOnStartup"], out var applyMigrationsOnStartup) && applyMigrationsOnStartup)
        {
            services.AddHostedService<DatabaseMigrationHostedService>();
        }

        return services;
    }
}



