using MedResearch.Application.Research;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Processing;
using MedResearch.Infrastructure.Literature.Persistence;
using MedResearch.Infrastructure.Literature.PubMed;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Research;
using MedResearch.Infrastructure.Research.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        services.AddScoped<IResearchRunQueue, PostgreSqlResearchRunQueue>();
        services.AddScoped<IScientificSearchResultStore, EfScientificSearchResultStore>();

        var pubMedOptions = CreatePubMedOptions(configuration);
        services.AddSingleton(Options.Create(pubMedOptions));
        services.AddSingleton<PubMedSearchResponseParser>();
        services.AddSingleton<PubMedArticleMapper>();
        services.AddHttpClient<PubMedScientificLiteratureSource>(client =>
        {
            client.BaseAddress = new Uri(pubMedOptions.BaseUrl, UriKind.Absolute);
            client.Timeout = pubMedOptions.Timeout;
        });
        services.AddScoped<IScientificLiteratureSource>(provider =>
            provider.GetRequiredService<PubMedScientificLiteratureSource>());

        var processingOptions = CreateResearchProcessingOptions(configuration);
        services.AddSingleton(Options.Create(processingOptions));

        if (processingOptions.Enabled)
        {
            services.AddHostedService<BackgroundResearchWorker>();
        }

        services.AddHealthChecks()
            .AddDbContextCheck<MedResearchDbContext>("postgresql", tags: ["database", "postgresql"]);

        if (bool.TryParse(configuration["Database:ApplyMigrationsOnStartup"], out var applyMigrationsOnStartup) && applyMigrationsOnStartup)
        {
            services.AddHostedService<DatabaseMigrationHostedService>();
        }

        return services;
    }

    private static ResearchProcessingOptions CreateResearchProcessingOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(ResearchProcessingOptions.SectionName);
        var enabled = true;
        var idleDelayMilliseconds = 1_000;

        if (bool.TryParse(section["Enabled"], out var configuredEnabled))
        {
            enabled = configuredEnabled;
        }

        if (int.TryParse(section["IdleDelayMilliseconds"], out var configuredIdleDelayMilliseconds))
        {
            idleDelayMilliseconds = configuredIdleDelayMilliseconds;
        }

        return new ResearchProcessingOptions
        {
            Enabled = enabled,
            IdleDelayMilliseconds = idleDelayMilliseconds
        };
    }

    private static PubMedOptions CreatePubMedOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(PubMedOptions.SectionName);

        return new PubMedOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(section["BaseUrl"])
                ? "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/"
                : section["BaseUrl"]!,
            Tool = string.IsNullOrWhiteSpace(section["Tool"])
                ? "MedResearch"
                : section["Tool"]!,
            Email = string.IsNullOrWhiteSpace(section["Email"])
                ? null
                : section["Email"],
            ApiKey = string.IsNullOrWhiteSpace(section["ApiKey"])
                ? null
                : section["ApiKey"],
            ResultLimit = TryReadInt(section["ResultLimit"], 10),
            TimeoutSeconds = TryReadInt(section["TimeoutSeconds"], 15),
            RequestIntervalMilliseconds = TryReadInt(section["RequestIntervalMilliseconds"], 350)
        };
    }

    private static int TryReadInt(string? value, int defaultValue)
    {
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
