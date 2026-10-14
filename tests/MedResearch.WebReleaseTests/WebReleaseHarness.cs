using System.Diagnostics;
using MedResearch.IntegrationTests;
using Npgsql;
using System.Text.Json;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MedResearch.Application.QuickResearch;
using MedResearch.Domain.QuickResearch;

namespace MedResearch.WebReleaseTests;

internal static class WebReleaseHarness
{
    public static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(["--quick-api"])) return await QuickAcceptanceApiHost.RunAsync();
        if (args.SequenceEqual(["--quick-local"]))
        {
            var path = Environment.GetEnvironmentVariable("MEDRESEARCH_QUICK_LOCAL_CONFIG") ?? throw new InvalidOperationException("Dedicated local Q4 configuration required.");
            using var data = JsonDocument.Parse(File.ReadAllText(path));
            var config = data.RootElement;
            if (config.GetProperty("Purpose").GetString() != "medresearch-quick-q4-local-v1") throw new InvalidOperationException("Not dedicated Q4 runtime.");
            var connection = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = config.GetProperty("Port").GetInt32(),
                Database = "medresearch_quick_q4", Username = "q4_local", Password = config.GetProperty("Password").GetString(), Timeout = 2, CommandTimeout = 3 };
            await using var database = new MedResearchDbContext(new DbContextOptionsBuilder<MedResearchDbContext>().UseNpgsql(connection.ConnectionString).Options);
            await database.Database.MigrateAsync();
            return await RunQuickAsync(connection.ConnectionString);
        }
        if (args.Length != 0) throw new InvalidOperationException("Unsupported verification mode.");
        Environment.SetEnvironmentVariable("MEDRESEARCH_REQUIRE_DOCKER_TESTS", "true");
        var fixture = new PostgreSqlFixture();
        try
        {
            await fixture.InitializeAsync();
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString!) { Timeout = 2, CommandTimeout = 3 };
            var runId = await FullFakePipelineTests.SeedBrowserScenarioAsync(connection.ConnectionString);
            var root = OpenApiContractTests.RepositoryRoot();
            var configuration = typeof(WebReleaseHarness).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
                .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "frontend/apps/web"), UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("e2e-fullstack/run.mjs");
            start.Environment["MEDRESEARCH_FULL_STACK"] = "true";
            start.Environment["MEDRESEARCH_FIXTURE_RUN_ID"] = runId.ToString();
            start.Environment["MEDRESEARCH_FIXTURE_CONTAINER_ID"] = fixture.ContainerId;
            start.Environment["ConnectionStrings__MedResearch"] = connection.ConnectionString;
            start.Environment["MEDRESEARCH_API_DLL"] = Path.Combine(root, $"src/MedResearch.Api/bin/{configuration}/net10.0/MedResearch.Api.dll");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Full-stack browser runner failed to start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            if (process.ExitCode != 0) return process.ExitCode;
            return await RunQuickAsync(connection.ConnectionString, fixture.ContainerId);
        }
        finally { await fixture.DisposeAsync(); }
    }

    private static async Task<int> RunQuickAsync(string connectionString, string? containerId = null)
    {
        var root = OpenApiContractTests.RepositoryRoot();
        var startedAt = DateTimeOffset.UtcNow;
        var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "frontend/apps/web"), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("e2e-quick/run.mjs");
        start.Environment["MEDRESEARCH_FULL_STACK"] = "true";
        start.Environment["MEDRESEARCH_QUICK_ACCEPTANCE"] = "true";
        start.Environment["MEDRESEARCH_QUICK_API_DLL"] = typeof(WebReleaseHarness).Assembly.Location;
        start.Environment["MEDRESEARCH_QUICK_ARTIFACTS"] = Path.Combine(root, "TestResults/QuickQ4");
        start.Environment["ConnectionStrings__MedResearch"] = connectionString;
        if (containerId is not null) start.Environment["MEDRESEARCH_FIXTURE_CONTAINER_ID"] = containerId;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Q4 browser runner failed to start.");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        // The outage case deliberately disconnects PostgreSQL; never reuse a stale pooled socket for diagnostics.
        var diagnosticConnection = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var db = new MedResearchDbContext(new DbContextOptionsBuilder<MedResearchDbContext>().UseNpgsql(diagnosticConnection.ConnectionString).Options);
        var saved = await db.QuickResearchRuns.AsNoTracking().OrderBy(r => r.CreatedAt).ToArrayAsync();
        var artifacts = Path.Combine(root, "TestResults/QuickQ4"); Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "persisted-quick-runs.json"), JsonSerializer.Serialize(saved,
            new JsonSerializerOptions { WriteIndented = true }));
        var events = File.ReadLines(Path.Combine(artifacts, "provider-events.jsonl"))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        var current = saved.Where(run => run.CreatedAt >= startedAt).ToArray();
        foreach (var run in current)
        {
            var sourceIds = run.Retrieval!.Sources.Select(source => source.Id).ToHashSet();
            var searches = events.Count(e => e.GetProperty("kind").GetString() == "scientific-search"
                && e.GetProperty("runId").GetGuid() == run.Id);
            var generations = events.Count(e => e.GetProperty("kind").GetString() == "structured-generation"
                && sourceIds.Contains(e.GetProperty("sourceId").GetGuid()));
            if (searches != 2 || generations != run.AnswerGeneration!.AttemptCount || generations > 1
                || run.Status is not (QuickResearchStatus.Completed or QuickResearchStatus.Failed)
                || run.LeaseOwner is not null || run.LeaseExpiresAt is not null)
                throw new InvalidOperationException($"Q4 persisted dispatch/terminal invariant failed: {run.Id}");
        }
        if (process.ExitCode == 0 && current.Length != 14) throw new InvalidOperationException("Q4/Q4.1 did not exercise all fourteen unique submitted runs.");
        Console.WriteLine($"Q4 fresh-context verification: {current.Length} runs; two fake searches/run; at most one generation; no terminal leases.");
        string[] questions = [
            "Помогают ли антибиотики при обычной простуде?",
            "Предотвращает ли витамин C простуду или помогает выздороветь быстрее?",
            "Are electronic cigarettes safer than smoking, and are they harmless?",
            "Как недосып влияет на способность запоминать информацию?",
            "Кофе продлевает жизнь?", "Уменьшает ли креатин тревожность?",
            "Лечат ли магнитные браслеты вирусные инфекции?", "Does vitamin C prevent the common cold?"
        ];
        var scorecard = questions.Select((question, index) => new {
            caseId = $"T{index + 1:00}", question, queryPlan = QuickQueryPreparation.Prepare(question),
            liveStatus = "NOT RUN", answer = (string?)null, selectedSources = (string[]?)null,
            supportVerdict = "cannot verify: no live retrieval/generation", contradictions = (string[]?)null,
            limitations = (string[]?)null, durationMs = (int?)null, dispatchedLiveAttempts = 0,
            measuredTokens = (int?)null, measuredCost = (decimal?)null
        });
        await File.WriteAllTextAsync(Path.Combine(artifacts, "control-question-scorecard.json"), JsonSerializer.Serialize(scorecard,
            new JsonSerializerOptions { WriteIndented = true }));
        return process.ExitCode;
    }
}
