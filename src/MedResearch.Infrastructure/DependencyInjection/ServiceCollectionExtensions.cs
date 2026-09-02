using MedResearch.Application.Research;
using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Evaluation;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Processing;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Infrastructure.Ai.OpenAI;
using MedResearch.Infrastructure.Extraction.Persistence;
using MedResearch.Infrastructure.Evaluation.Persistence;
using MedResearch.Infrastructure.Literature.Persistence;
using MedResearch.Infrastructure.Literature.PubMed;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Planning.Persistence;
using MedResearch.Infrastructure.Research;
using MedResearch.Infrastructure.Research.Processing;
using MedResearch.Infrastructure.Synthesis.Persistence;
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

        services.AddDbContext<MedResearchDbContext>(options => ConfigurePostgreSql(options, connectionString));
        services.AddDbContextFactory<MedResearchDbContext>(options => ConfigurePostgreSql(options, connectionString), ServiceLifetime.Scoped);

        services.AddScoped<IResearchStore, EfResearchStore>();
        services.AddScoped<IResearchRunQueue>(provider =>
            new PostgreSqlResearchRunQueue(provider.GetRequiredService<IDbContextFactory<MedResearchDbContext>>()));
        services.AddScoped<IResearchPlanStore, EfResearchPlanStore>();
        services.AddScoped<IScientificSearchResultStore, EfScientificSearchResultStore>();
        services.AddScoped<IEvidenceExtractionStore, EfEvidenceExtractionStore>();
        services.AddScoped<IEvidenceEvaluationStore, EfEvidenceEvaluationStore>();
        services.AddScoped<EfResearchSynthesisStore>();
        services.AddScoped<ISynthesisCorpusStore>(provider => provider.GetRequiredService<EfResearchSynthesisStore>());
        services.AddScoped<IResearchReportStore>(provider => provider.GetRequiredService<EfResearchSynthesisStore>());

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
        services.AddSingleton<IPubMedRequestGate, TokenBucketPubMedRequestGate>();
        services.AddSingleton<IPubMedRetryDelay, PubMedRetryDelay>();
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

        var evidenceEvaluationOptions = CreateEvidenceEvaluationOptions(configuration);
        services.AddSingleton(evidenceEvaluationOptions);

        var synthesisOptions = CreateSynthesisOptions(configuration);
        services.AddSingleton(synthesisOptions);

        var processingOptions = CreateResearchProcessingOptions(configuration);
        services.AddSingleton(Options.Create(processingOptions));

        if (processingOptions.Enabled)
        {
            services.AddHostedService<BackgroundResearchWorker>();
        }

        services.AddHealthChecks()
            .AddDbContextCheck<MedResearchDbContext>("postgresql", tags: ["database", "postgresql", "ready"]);

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
            MaxStudiesPerRun = ReadPositiveInt(section["MaxStudiesPerRun"], 10, "EvidenceExtraction:MaxStudiesPerRun")
        };
    }

    private static EvidenceEvaluationOptions CreateEvidenceEvaluationOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(EvidenceEvaluationOptions.SectionName);

        return new EvidenceEvaluationOptions
        {
            MaxStudiesPerRun = ReadPositiveInt(section["MaxStudiesPerRun"], 10, "EvidenceEvaluation:MaxStudiesPerRun")
        };
    }

    private static SynthesisOptions CreateSynthesisOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(SynthesisOptions.SectionName);

        return new SynthesisOptions
        {
            MaxStudies = ReadPositiveInt(section["MaxStudies"], 10, "Synthesis:MaxStudies"),
            MaxEvidenceFindings = ReadPositiveInt(section["MaxEvidenceFindings"], 40, "Synthesis:MaxEvidenceFindings"),
            MaxClaims = ReadPositiveInt(section["MaxClaims"], 12, "Synthesis:MaxClaims")
        };
    }

    private static ResearchProcessingOptions CreateResearchProcessingOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(ResearchProcessingOptions.SectionName);
        var enabled = true;
        var idleDelayMilliseconds = ReadPositiveInt(section["IdleDelayMilliseconds"], 1_000, "ResearchProcessing:IdleDelayMilliseconds");
        var leaseDurationSeconds = ReadPositiveInt(section["LeaseDurationSeconds"], 900, "ResearchProcessing:LeaseDurationSeconds");
        var heartbeatIntervalSeconds = ReadPositiveInt(section["HeartbeatIntervalSeconds"], 60, "ResearchProcessing:HeartbeatIntervalSeconds");

        if (bool.TryParse(section["Enabled"], out var configuredEnabled))
        {
            enabled = configuredEnabled;
        }

        if (heartbeatIntervalSeconds >= leaseDurationSeconds)
        {
            throw new InvalidOperationException("ResearchProcessing:HeartbeatIntervalSeconds must be shorter than ResearchProcessing:LeaseDurationSeconds.");
        }

        return new ResearchProcessingOptions
        {
            Enabled = enabled,
            IdleDelayMilliseconds = idleDelayMilliseconds,
            LeaseDurationSeconds = leaseDurationSeconds,
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds
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
            TimeoutSeconds = ReadPositiveInt(section["TimeoutSeconds"], 30, "AI:TimeoutSeconds"),
            MaxOutputTokens = ReadPositiveInt(section["MaxOutputTokens"], 2_000, "AI:MaxOutputTokens")
        };
    }

    private static PubMedOptions CreatePubMedOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(PubMedOptions.SectionName);
        var enabled = true;
        if (bool.TryParse(section["Enabled"], out var configuredEnabled))
        {
            enabled = configuredEnabled;
        }

        var options = new PubMedOptions
        {
            Enabled = enabled,
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
            MaxResultsPerQuery = ReadPositiveInt(section["MaxResultsPerQuery"] ?? section["ResultLimit"], 10, "PubMed:MaxResultsPerQuery"),
            TimeoutSeconds = ReadPositiveInt(section["TimeoutSeconds"], 15, "PubMed:TimeoutSeconds"),
            MaxRequestsPerSecond = ReadMaxRequestsPerSecond(section),
            FetchBatchSize = ReadPositiveInt(section["FetchBatchSize"], 25, "PubMed:FetchBatchSize"),
            MaxRetryAttempts = ReadNonNegativeInt(section["MaxRetryAttempts"], 2, "PubMed:MaxRetryAttempts"),
            RetryBaseDelayMilliseconds = ReadPositiveInt(section["RetryBaseDelayMilliseconds"], 250, "PubMed:RetryBaseDelayMilliseconds")
        };

        options.Validate();
        return options;
    }

    private static int ReadMaxRequestsPerSecond(IConfigurationSection section)
    {
        if (!string.IsNullOrWhiteSpace(section["MaxRequestsPerSecond"]))
        {
            return ReadPositiveInt(section["MaxRequestsPerSecond"], 2, "PubMed:MaxRequestsPerSecond");
        }

        if (!string.IsNullOrWhiteSpace(section["RequestIntervalMilliseconds"]))
        {
            var interval = ReadPositiveInt(section["RequestIntervalMilliseconds"], 350, "PubMed:RequestIntervalMilliseconds");
            return Math.Max(1, 1_000 / interval);
        }

        return 2;
    }

    private static void ConfigurePostgreSql(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsqlOptions =>
            npgsqlOptions.MigrationsAssembly(typeof(MedResearchDbContext).Assembly.FullName));
    }

    private static int ReadPositiveInt(string? value, int defaultValue, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Configuration value {configurationKey} must be an integer.");
        }

        if (parsed <= 0)
        {
            throw new InvalidOperationException($"Configuration value {configurationKey} must be positive.");
        }

        return parsed;
    }

    private static int ReadNonNegativeInt(string? value, int defaultValue, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Configuration value {configurationKey} must be an integer.");
        }

        if (parsed < 0)
        {
            throw new InvalidOperationException($"Configuration value {configurationKey} must be zero or positive.");
        }

        return parsed;
    }
}
