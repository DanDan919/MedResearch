using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Planning;
using MedResearch.Infrastructure.Ai.CodexCli;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedResearch.LiveE2EValidationTests;

public sealed class CodexCliLiveSmokeTests
{
    [SkippableFact]
    public async Task CodexCli_ReturnsStrictStructuredProbe()
    {
        Skip.IfNot(Truthy("MEDRESEARCH_RUN_LIVE_CODEX_CLI"), "Set MEDRESEARCH_RUN_LIVE_CODEX_CLI=true to consume Codex allowance.");

        var startedAt = DateTimeOffset.UtcNow;
        var result = await CreateClient().GenerateStructuredAsync<Probe>(
            new StructuredLlmRequest(
                "codex-cli-smoke-v1",
                "Return only the requested structured result. Do not use shell, web, files, or external research.",
                "Return status=ok and provider=codex-cli.",
                new StructuredOutputSchema(
                    "codex_cli_smoke",
                    "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"status\",\"provider\"],\"properties\":{\"status\":{\"type\":\"string\",\"enum\":[\"ok\"]},\"provider\":{\"type\":\"string\",\"enum\":[\"codex-cli\"]}}}")),
            CancellationToken.None);

        Assert.Equal("ok", result.Value.Status);
        Assert.Equal("codex-cli", result.Value.Provider);
        Assert.Equal("codex-cli", result.Metadata.Provider);
        Assert.True(DateTimeOffset.UtcNow - startedAt < TimeSpan.FromMinutes(10));
    }

    [SkippableFact]
    public async Task CodexCli_PlannerOutputPassesExistingApplicationValidation()
    {
        Skip.IfNot(Truthy("MEDRESEARCH_RUN_LIVE_CODEX_ROLES"), "Set MEDRESEARCH_RUN_LIVE_CODEX_ROLES=true to run role-level Codex checks.");

        const string question = "What is the evidence for creatine supplementation on cognitive performance in healthy adults?";
        var prompt = ResearchPlannerPrompt.Create(question, 2);
        var result = await CreateClient().GenerateStructuredAsync<ResearchPlanDraft>(
            new StructuredLlmRequest(
                ResearchPlannerPrompt.Version,
                prompt.SystemPrompt,
                prompt.UserPrompt,
                ResearchPlannerPrompt.CreateOutputSchema(2)),
            CancellationToken.None);

        var plan = ResearchPlanValidator.CreateValidatedPlan(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            question,
            result.Value,
            result.Metadata,
            ResearchPlannerPrompt.Version,
            2);

        Assert.Equal("codex-cli", plan.Provider);
        Assert.InRange(plan.SearchQueries.Length, 1, 2);
    }

    private static CodexCliStructuredLlmClient CreateClient()
    {
        var options = new CodexCliOptions
        {
            ExecutablePath = Environment.GetEnvironmentVariable("AI__CodexCli__ExecutablePath") ?? "codex",
            Model = Environment.GetEnvironmentVariable("AI__CodexCli__Model"),
            TimeoutSeconds = 300,
            MaxPromptCharacters = 500_000,
            Sandbox = "read-only"
        };

        return new CodexCliStructuredLlmClient(
            Options.Create(options),
            new CodexCliProcessRunner(),
            NullLogger<CodexCliStructuredLlmClient>.Instance);
    }

    private static bool Truthy(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is not null
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value == "1"
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record Probe(string Status, string Provider);
}
