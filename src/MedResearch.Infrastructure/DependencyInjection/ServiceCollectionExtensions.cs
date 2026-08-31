using MedResearch.Application.Research;
using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Processing;
using MedResearch.Infrastructure.Ai.OpenAI;
using MedResearch.Infrastructure.Extraction.Persistence;
using MedResearch.Infrastructure.Literature.Persistence;
using MedResearch.Infrastructure.Literature.PubMed;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Planning.Persistence;
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
        services.AddScoped<IResearchPlanStore, EfResearchPlanStore>();
        services.AddScoped<IScientificSearchResultStore, EfScientificSearchResultStore>();
        services.AddScoped<IEvidenceExtractionStore, EfEvidenceExtractionStore>();

        var openAIOptions = CreateOpenAIOptions(configuration);
        if (!string.Equals(openAIOptions.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AI provider '{openAIOptions.Provider}' is not supported. Supported provider: OpenAI.");
        }

        services.AddSingleton(Options.Create(openAIOptions));
        services.AddHttpClient<OpenAIStructuredLlmClient>(client =>
        {
            client.BaseAddress = new Uri(openAIOptions.BaseUrl, UriKind.Absolute);
            client.Timeout = openAIOptions.Timeout;
        });
        services.AddScoped<IStructuredLlmClient>(provider =>
            provider.GetRequiredService<OpenAIStructuredLlmClient>());

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

        var evidenceExtractionOptions = CreateEvidenceExtractionOptions(configuration);
        services.AddSingleton(evidenceExtractionOptions);

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

    private static EvidenceExtractionOptions CreateEvidenceExtractionOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(EvidenceExtractionOptions.SectionName);

        return new EvidenceExtractionOptions
        {
            MaxStudiesPerRun = TryReadInt(section["MaxStudiesPerRun"], 10)
        };
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

    private static OpenAIOptions CreateOpenAIOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(OpenAIOptions.SectionName);

        return new OpenAIOptions
        {
            Provider = string.IsNullOrWhiteSpace(section["Provider"])
                ? "OpenAI"
                : section["Provider"]!,
            BaseUrl = string.IsNullOrWhiteSpace(section["BaseUrl"])
                ? "https://api.openai.com/v1/"
                : section["BaseUrl"]!,
            Model = string.IsNullOrWhiteSpace(section["Model"])
                ? null
                : section["Model"],
            ApiKey = string.IsNullOrWhiteSpace(section["ApiKey"])
                ? null
                : section["ApiKey"],
            TimeoutSeconds = TryReadInt(section["TimeoutSeconds"], 30),
            MaxOutputTokens = TryReadInt(section["MaxOutputTokens"], 2_000)
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
