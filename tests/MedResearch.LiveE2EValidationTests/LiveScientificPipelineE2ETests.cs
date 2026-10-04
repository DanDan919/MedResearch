
using System.Net;
using System.Net.Http.Json;
using MedResearch.Api.Research;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MedResearch.LiveE2EValidationTests;

public sealed class LiveScientificPipelineE2ETests
{
    private const string Question = "In healthy adults, does creatine supplementation improve cognitive performance compared with placebo?";

    [SkippableFact]
    public async Task ResearchPipeline_WithRealProviders_CompletesBoundedLiveRun()
    {
        Skip.IfNot(Truthy("MEDRESEARCH_RUN_LIVE_E2E"), "Live E2E is opt-in only.");
        var connection = Env("MEDRESEARCH_LIVE_E2E_CONNECTION_STRING") ?? Env("ConnectionStrings__MedResearch");
        Skip.If(string.IsNullOrWhiteSpace(connection), "Set an isolated PostgreSQL connection string.");
        Skip.IfNot(string.Equals(Env("MEDRESEARCH_LIVE_E2E_DATABASE_ACK"), "isolated", StringComparison.OrdinalIgnoreCase), "Set MEDRESEARCH_LIVE_E2E_DATABASE_ACK=isolated.");
        var provider = Env("MEDRESEARCH_LLM_PROVIDER") ?? "OpenAI";
        var apiKey = Env("AI__ApiKey") ?? Env("OPENAI_API_KEY");
        var model = Env("AI__Model") ?? Env("OPENAI_MODEL");
        var email = Env("PubMed__Email") ?? Env("PUBMED_EMAIL");
        Skip.If(string.IsNullOrWhiteSpace(email), "Set PubMed__Email or PUBMED_EMAIL.");
        if (string.Equals(provider, "CodexCli", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "codex-cli", StringComparison.OrdinalIgnoreCase))
        {
            Skip.IfNot(Truthy("MEDRESEARCH_RUN_LIVE_CODEX_CLI"), "Set MEDRESEARCH_RUN_LIVE_CODEX_CLI=true for real Codex E2E.");
        }
        else
        {
            Skip.If(string.IsNullOrWhiteSpace(apiKey), "Set OPENAI_API_KEY or AI__ApiKey.");
            Skip.If(string.IsNullOrWhiteSpace(model), "Set OPENAI_MODEL or AI__Model.");
        }

        await using var factory = new LiveFactory();
        using var client = factory.CreateClient();
        var readiness = await client.GetAsync("/health/ready");
        var readinessBody = await readiness.Content.ReadAsStringAsync();
        Assert.True(
            readiness.StatusCode == HttpStatusCode.OK,
            $"Readiness returned {(int)readiness.StatusCode} {readiness.StatusCode}: {readinessBody}");

        var createdResponse = await client.PostAsJsonAsync("/api/research", new CreateResearchRequest(Question));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateResearchResponse>();
        Assert.NotNull(created);

        var run = await WaitForTerminalRunAsync(client, created.ResearchRunId);
        Assert.True(
            string.Equals(run.Status, "Completed", StringComparison.Ordinal),
            $"Live research run ended with status {run.Status}. FailureReason: {run.FailureReason ?? "<none>"}");
        Assert.Null(run.FailureReason);

        var reportResponse = await client.GetAsync($"/api/research/{created.ResearchRunId}/report");
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<ResearchReportResponse>();
        Assert.NotNull(report);
        Assert.True(report.Coverage.SearchQueryCount is >= 1 and <= 4);
        Assert.True(report.Coverage.DiscoveredStudyCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(report.Conclusion));
    }

    private static async Task<ResearchRunResponse> WaitForTerminalRunAsync(HttpClient client, Guid runId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        while (true)
        {
            var response = await client.GetAsync($"/api/research/{runId}", timeout.Token);
            response.EnsureSuccessStatusCode();
            var run = await response.Content.ReadFromJsonAsync<ResearchRunResponse>(timeout.Token);
            Assert.NotNull(run);
            if (run.Status is "Completed" or "Failed" or "Cancelled") return run;
            await Task.Delay(TimeSpan.FromSeconds(5), timeout.Token);
        }
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
    private static bool Truthy(string name) => Env(name) is { } value && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private sealed class LiveFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
        }
    }
}
