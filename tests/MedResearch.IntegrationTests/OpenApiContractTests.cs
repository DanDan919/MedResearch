using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MedResearch.IntegrationTests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task ActualBackendDocument_MatchesCanonicalSnapshot()
    {
        using var factory = new ContractFactory();
        using var client = factory.CreateClient();
        var actual = Normalize(JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!);
        var snapshot = Path.Combine(RepositoryRoot(), "frontend", "packages", "api", "openapi", "medresearch-api.json");
        if (Environment.GetEnvironmentVariable("MEDRESEARCH_UPDATE_OPENAPI") == "true")
            await File.WriteAllTextAsync(snapshot, actual.ToJsonString(new() { WriteIndented = true }) + "\n");
        var expected = Normalize(JsonNode.Parse(await File.ReadAllTextAsync(snapshot))!);
        var create = actual["paths"]!["/api/research"]!["post"]!;
        var key = create["parameters"]!.AsArray().Single(parameter => parameter!["name"]!.GetValue<string>() == "Idempotency-Key")!;
        Assert.True(key["required"]!.GetValue<bool>());
        Assert.Equal("uuid", key["schema"]!["format"]!.GetValue<string>());
        foreach (var status in new[] { "400", "409", "429", "503" }) Assert.NotNull(create["responses"]![status]);
        Assert.True(JsonNode.DeepEquals(expected, actual), "Backend OpenAPI drift: regenerate the canonical snapshot with MEDRESEARCH_UPDATE_OPENAPI=true, review, then pnpm api:generate.");
        foreach (var path in actual["paths"]!.AsObject().Where(path => path.Key.StartsWith("/api/research", StringComparison.Ordinal)))
            foreach (var operation in path.Value!.AsObject()) Assert.NotNull(operation.Value!["security"]);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("schema")]
    [InlineData("security")]
    [InlineData("nullable")]
    public async Task DriftComparison_DetectsIntentionalContractMutation(string kind)
    {
        var expected = Normalize(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "frontend/packages/api/openapi/medresearch-api.json")))!);
        var mutated = expected.DeepClone();
        if (kind == "path") mutated["paths"]!.AsObject().Remove(mutated["paths"]!.AsObject().First().Key);
        else if (kind == "schema") mutated["components"]!["schemas"]!["CreateResearchRequest"]!["properties"]!["question"]!["maxLength"] = 7;
        else if (kind == "security") mutated["components"]!["securitySchemes"]!.AsObject().Clear();
        else mutated["components"]!["schemas"]!["CreateResearchRequest"]!["properties"]!["question"]!["type"] = new JsonArray("string", "null");
        Assert.False(JsonNode.DeepEquals(expected, Normalize(mutated)));
    }

    private static JsonNode Normalize(JsonNode node)
    {
        var copy = node.DeepClone();
        // The sole exclusion is the runtime test origin, not paths, schemas or security.
        copy.AsObject().Remove("servers");
        return Sort(copy);
    }

    private static JsonNode Sort(JsonNode node) => node switch
    {
        JsonObject map => new JsonObject(map.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value is null ? null : Sort(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(value => value is null ? null : Sort(value)).ToArray()),
        _ => node.DeepClone()
    };

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MedResearch.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root unavailable");
    }

    private sealed class ContractFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Authentication:Mode", "JwtBearer");
            builder.UseSetting("Authentication:Authority", "https://contract-issuer.example.org/");
            builder.UseSetting("Authentication:Audience", "contract-api");
            builder.UseSetting("ResearchProcessing:Enabled", "false");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
