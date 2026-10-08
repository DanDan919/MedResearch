using System.Diagnostics;
using MedResearch.IntegrationTests;
using Npgsql;

namespace MedResearch.WebReleaseTests;

internal static class WebReleaseHarness
{
    public static async Task<int> Main()
    {
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
            return process.ExitCode;
        }
        finally { await fixture.DisposeAsync(); }
    }
}
