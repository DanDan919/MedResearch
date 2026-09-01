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
            ["PubMed:ResultLimit"] = "10",
            ["PubMed:TimeoutSeconds"] = "15",
            ["PubMed:RequestIntervalMilliseconds"] = "350"
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
