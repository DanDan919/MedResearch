using MedResearch.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace MedResearch.IntegrationTests;

public sealed class AuthenticationConfigurationTests
{
    [Fact]
    public void DevelopmentLocalMode_IsRejectedOutsideDevelopment()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = "DevelopmentLocal"
        });

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddMedResearchAuthentication(configuration, Environment("Production")));
    }

    [Fact]
    public void JwtBearerMode_RequiresAuthorityAndAudience()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = "JwtBearer"
        });

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddMedResearchAuthentication(configuration, Environment("Production")));
    }

    [Fact]
    public void DevelopmentLocalMode_IsExplicitlyAllowedInDevelopment()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = "DevelopmentLocal",
            ["Authentication:DevelopmentSubject"] = "local-test-user"
        });

        var services = new ServiceCollection();

        services.AddMedResearchAuthentication(configuration, Environment("Development"));

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(Microsoft.AspNetCore.Authorization.IAuthorizationHandler));
    }

    [Theory]
    [InlineData("http://issuer.example.org")]
    [InlineData("https://user:password@issuer.example.org")]
    [InlineData("https://issuer.example.org?token=not-a-secret")]
    [InlineData("https://issuer.example.org#fragment")]
    [InlineData("not-a-url")]
    public void ProductionJwtAuthority_RejectsUnsafeUrls(string authority)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        { ["Authentication:Mode"] = "JwtBearer", ["Authentication:Authority"] = authority, ["Authentication:Audience"] = "research-api" });
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMedResearchAuthentication(configuration, Environment("Production")));
        Assert.DoesNotContain(authority, error.Message);
    }

    [Fact]
    public void ProductionJwtAuthority_AcceptsHttpsIssuer()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        { ["Authentication:Mode"] = "JwtBearer", ["Authentication:Authority"] = "https://issuer.example.org/tenant", ["Authentication:Audience"] = "research-api" });
        new ServiceCollection().AddMedResearchAuthentication(configuration, Environment("Production"));
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IWebHostEnvironment Environment(string name)
    {
        return new TestWebHostEnvironment { EnvironmentName = name };
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "MedResearch.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
