using MedResearch.Application.Research.Literature;
using MedResearch.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedResearch.Infrastructure.Tests.DependencyInjection;

public sealed class InfrastructureConfigurationTests
{
    [Fact]
    public void AddInfrastructure_RejectsHeartbeatIntervalAtOrAboveLeaseDuration()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ResearchProcessing:LeaseDurationSeconds"] = "30",
            ["ResearchProcessing:HeartbeatIntervalSeconds"] = "30"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("HeartbeatIntervalSeconds", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_RejectsNonPositiveNumericConfiguration()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ResearchProcessing:LeaseDurationSeconds"] = "0"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("ResearchProcessing:LeaseDurationSeconds", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_DoesNotRequireOpenAiApiKeyAtStartup()
    {
        var configuration = CreateConfiguration([]);
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider);
    }

    [Fact]
    public void AddInfrastructure_RegistersEnabledScientificLiteratureSources()
    {
        var configuration = CreateConfiguration([]);
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var sources = provider.GetServices<IScientificLiteratureSource>().Select(source => source.SourceName).Order().ToArray();

        Assert.Equal([ScientificLiteratureSourceNames.EuropePmc, ScientificLiteratureSourceNames.PubMed], sources);
    }

    [Fact]
    public void AddInfrastructure_RejectsPubMedRateAboveOfficialLimitWithoutApiKey()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["PubMed:MaxRequestsPerSecond"] = "4"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("PubMed:MaxRequestsPerSecond", exception.Message);
        Assert.Contains("without an API key", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_AllowsPubMedKeyedRateUpToOfficialDefaultLimit()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["PubMed:ApiKey"] = "test-api-key",
            ["PubMed:MaxRequestsPerSecond"] = "10"
        });
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider);
    }

    [Fact]
    public void AddInfrastructure_RejectsPubMedRateAboveOfficialDefaultLimitWithApiKey()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["PubMed:ApiKey"] = "test-api-key",
            ["PubMed:MaxRequestsPerSecond"] = "11"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("PubMed:MaxRequestsPerSecond", exception.Message);
        Assert.Contains("with an API key", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_RejectsPubMedInvalidBatchAndRetryConfiguration()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["PubMed:FetchBatchSize"] = "0"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("PubMed:FetchBatchSize", exception.Message);
    }

    [Theory]
    [InlineData("EuropePmc:MaxResultsPerQuery", "201")]
    [InlineData("EuropePmc:PageSize", "101")]
    [InlineData("EuropePmc:MaxRequestsPerSecond", "6")]
    [InlineData("EuropePmc:MaxRetryAttempts", "6")]
    [InlineData("EuropePmc:RetryBaseDelayMilliseconds", "0")]
    public void AddInfrastructure_RejectsInvalidEuropePmcBounds(string key, string value)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            [key] = value
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains(key, exception.Message);
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MedResearch"] = "Host=localhost;Port=5432;Database=medresearch_tests;Username=medresearch;Password=development_only",
            ["ResearchProcessing:Enabled"] = "false",
            ["ResearchProcessing:IdleDelayMilliseconds"] = "1000",
            ["ResearchProcessing:LeaseDurationSeconds"] = "900",
            ["ResearchProcessing:HeartbeatIntervalSeconds"] = "60",
            ["AI:Provider"] = "OpenAI",
            ["AI:BaseUrl"] = "https://api.openai.com/v1/",
            ["AI:TimeoutSeconds"] = "30",
            ["AI:MaxOutputTokens"] = "2000",
            ["PubMed:BaseUrl"] = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/",
            ["PubMed:MaxResultsPerQuery"] = "10",
            ["PubMed:TimeoutSeconds"] = "15",
            ["PubMed:MaxRequestsPerSecond"] = "2",
            ["PubMed:FetchBatchSize"] = "25",
            ["PubMed:MaxRetryAttempts"] = "2",
            ["PubMed:RetryBaseDelayMilliseconds"] = "250",
            ["EuropePmc:BaseUrl"] = "https://www.ebi.ac.uk/europepmc/webservices/rest/",
            ["EuropePmc:MaxResultsPerQuery"] = "10",
            ["EuropePmc:PageSize"] = "25",
            ["EuropePmc:TimeoutSeconds"] = "15",
            ["EuropePmc:MaxRequestsPerSecond"] = "2",
            ["EuropePmc:MaxRetryAttempts"] = "2",
            ["EuropePmc:RetryBaseDelayMilliseconds"] = "250"
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
