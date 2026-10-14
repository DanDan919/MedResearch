using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Application.Research.Ai;
using MedResearch.Domain.QuickResearch;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace MedResearch.WebReleaseTests;

// Real API composition, Kestrel, JWT, worker and EF; only scientific/LLM ports are fake.
internal static class QuickAcceptanceApiHost
{
    public static async Task<int> RunAsync()
    {
        RequireDedicatedDatabase();
        using var stopped = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stopped.Cancel(); };
        using var signal = OperatingSystem.IsWindows() ? null : System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, context => { context.Cancel = true; stopped.Cancel(); });
        using var factory = new QuickFactory();
        factory.UseKestrel(int.Parse(Environment.GetEnvironmentVariable("MEDRESEARCH_QUICK_API_PORT") ?? "3442"));
        factory.StartServer();
        Console.WriteLine("Q4 TEST HOST: synthetic scientific sources and structured LLM only; no clinical/live validation.");
        try { await Task.Delay(Timeout.Infinite, stopped.Token); } catch (OperationCanceledException) { }
        return 0;
    }

    private static void RequireDedicatedDatabase()
    {
        if (Environment.GetEnvironmentVariable("MEDRESEARCH_QUICK_ACCEPTANCE") != "true") throw new InvalidOperationException("Explicit Q4 acceptance mode required.");
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__MedResearch"));
        if (connection.Database == "medresearch_tests"
            && Environment.GetEnvironmentVariable("MEDRESEARCH_REQUIRE_DOCKER_TESTS") == "true"
            && System.Text.RegularExpressions.Regex.IsMatch(Environment.GetEnvironmentVariable("MEDRESEARCH_FIXTURE_CONTAINER_ID") ?? "", "^[a-f0-9]{64}$")) return;
        var path = Environment.GetEnvironmentVariable("MEDRESEARCH_QUICK_LOCAL_CONFIG") ?? throw new InvalidOperationException("Dedicated Q4 database acknowledgement required.");
        using var config = JsonDocument.Parse(File.ReadAllText(path));
        if (config.RootElement.GetProperty("Purpose").GetString() != "medresearch-quick-q4-local-v1"
            || connection.Host != "127.0.0.1" || connection.Port != config.RootElement.GetProperty("Port").GetInt32()
            || connection.Database != "medresearch_quick_q4" || connection.Username != "q4_local"
            || connection.Password != config.RootElement.GetProperty("Password").GetString())
            throw new InvalidOperationException("Not the acknowledged dedicated Q4 database.");
    }

    private sealed class QuickFactory : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.Combine(MedResearch.IntegrationTests.OpenApiContractTests.RepositoryRoot(), "src/MedResearch.Api"));
            builder.UseEnvironment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQuickScientificSources>();
                services.AddSingleton<IQuickScientificSources, FakeSources>();
                services.RemoveAll<IStructuredLlmClient>();
                services.AddSingleton<IStructuredLlmClient, FakeAnswer>();
                services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(
                    http => http.AdditionalHandlers.Add(new LoopbackOnlyHandler())));
                var caPath = Environment.GetEnvironmentVariable("SSL_CERT_FILE");
                if (!string.IsNullOrEmpty(caPath))
                {
                    var ca = X509CertificateLoader.LoadCertificateFromFile(caPath);
                    services.Configure<JwtBearerOptions>("Bearer", options => options.BackchannelHttpHandler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
                        {
                            if (request.RequestUri?.Host != "127.0.0.1" || certificate is null
                                || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
                            using var chain = new X509Chain();
                            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            chain.ChainPolicy.CustomTrustStore.Add(ca);
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            return chain.Build(certificate);
                        }
                    });
                }
            });
        }
    }

    private sealed class LoopbackOnlyHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.Host is not ("localhost" or "127.0.0.1")) throw new InvalidOperationException("Unexpected external HTTP in Q4 acceptance host.");
            return base.SendAsync(request, token);
        }
    }

    private static string Scenario => Environment.GetEnvironmentVariable("MEDRESEARCH_QUICK_FAKE_SCENARIO") ?? "normal";
    private static readonly object AuditLock = new();
    private static void Audit(object value)
    {
        var root = Environment.GetEnvironmentVariable("MEDRESEARCH_QUICK_ARTIFACTS") ?? throw new InvalidOperationException("Q4 artifacts directory required.");
        Directory.CreateDirectory(root);
        lock (AuditLock) File.AppendAllText(Path.Combine(root, "provider-events.jsonl"), JsonSerializer.Serialize(value) + "\n");
    }

    private sealed class FakeSources : IQuickScientificSources
    {
        public string[] EnabledProviders => ["PubMed", "EuropePmc"];
        public string PrepareQuery(string provider, string query) => query;
        public async Task<QuickProviderPage> SearchAsync(string provider, Guid runId, Guid attemptId, string query, CancellationToken token)
        {
            Audit(new { kind = "scientific-search", fake = true, runId, attemptId, provider, query, scenario = Scenario, startedAt = DateTimeOffset.UtcNow });
            await Task.Delay(100, token);
            if (Scenario == "failed" || Scenario == "partial" && provider == "EuropePmc") throw new HttpRequestException("Synthetic scientific provider unavailable.");
            if (Scenario == "zero") return new(0, [], 0);
            return new(1, [new("90000001", null, null, "TEST ONLY: sleep deprivation and memory in adults",
                Scenario == "no-abstract" ? null : "This synthetic abstract describes sleep deprivation and memory in adults; it does not report a medical conclusion.",
                "Test fixture journal", null, 2026, ["Synthetic Author"], ["Journal Article"], new(attemptId, provider, "90000001"))], 0);
        }
    }

    private sealed class FakeAnswer : IStructuredLlmClient
    {
        public async Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken token)
        {
            if (request.PromptVersion != QuickAnswerPrompt.Version) throw new InvalidOperationException("Deep generation is forbidden in Q4 host.");
            using var input = JsonDocument.Parse(request.UserPrompt);
            var source = input.RootElement.GetProperty("publications")[0];
            var id = source.GetProperty("sourceId").GetGuid();
            Audit(new { kind = "structured-generation", fake = true, sourceId = id, scenario = Scenario,
                provider = "DeterministicFake", model = "q4-fake-v1", configuredLimits = request.Limits,
                input = input.RootElement.Clone(), startedAt = DateTimeOffset.UtcNow, measuredTokens = (int?)null, measuredCost = (decimal?)null });
            if (Scenario == "timeout") await Task.Delay(Timeout.Infinite, token);
            if (Scenario == "invalid-json") throw new JsonException("Synthetic malformed model response.");
            const string text = "The synthetic source describes sleep deprivation and memory in adults; this is not a medical answer.";
            var citationId = Scenario == "invalid-citation" ? Guid.NewGuid() : id;
            var draft = new QuickAnswerDraft(text, [new(text, [new(citationId, source.GetProperty("abstractText").GetString()!)])], [],
                ["This synthetic fixture cannot establish any medical effect."], "VeryLimited", [citationId]);
            return new((T)(object)draft, new("DeterministicFake", "q4-fake-v1", null, DateTimeOffset.UtcNow));
        }
    }
}
