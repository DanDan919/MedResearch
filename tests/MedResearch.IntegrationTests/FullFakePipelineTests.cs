using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MedResearch.Api.Research;
using MedResearch.Application.Research.Ai;
using MedResearch.Application.Research.Extraction;
using MedResearch.Application.Research.Evaluation;
using MedResearch.Application.Research.Literature;
using MedResearch.Application.Research.Planning;
using MedResearch.Application.Research.Processing;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Research.Processing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed partial class FullFakePipelineTests
{
    private readonly PostgreSqlFixture _fixture;

    public FullFakePipelineTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ResearchPipeline_WithFakeProviders_CompletesAndReturnsTraceableReport()
    {
        SkipIfPostgreSqlUnavailable();

        await using (var db = _fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE research_runs
                SET status = 'Cancelled',
                    completed_at = NOW(),
                    processing_lease_owner = NULL,
                    processing_lease_acquired_at = NULL,
                    processing_lease_expires_at = NULL,
                    last_heartbeat_at = NULL
                WHERE status NOT IN ('Completed', 'Failed', 'Cancelled');
                """,
                CancellationToken.None);
        }

        var fakeLlm = new FakeStructuredLlmClient();
        var fakeLiterature = new FakeScientificLiteratureSource();
        using var factory = new FakePipelineApiFactory(_fixture.ConnectionString!, fakeLlm, fakeLiterature);
        using var client = factory.CreateClient();
        const string question = "Does structured sleep improve recall in adults?";

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/research", new CreateResearchRequest(question));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateResearchResponse>();
        Assert.NotNull(created);

        using (var scope = factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ResearchRunProcessor>();
            var processed = await processor.ProcessNextQueuedRunAsync(
                "fake-e2e-worker",
                TimeSpan.FromMinutes(5),
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
            Assert.True(processed);
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var run = await db.ResearchRuns.SingleAsync(run => run.Id == created.ResearchRunId, CancellationToken.None);
            Assert.True(run.Status == ResearchRunStatus.Completed, $"Expected research run to complete, but status was {run.Status}. FailureReason: {run.FailureReason}. Logs: {factory.LogProvider.Messages}");
            Assert.Null(run.ProcessingLeaseOwner);
            Assert.Null(run.ProcessingLeaseExpiresAt);

            Assert.True(await db.ResearchPlans.AnyAsync(plan => plan.ResearchRunId == created.ResearchRunId, CancellationToken.None));
            Assert.True(await db.LiteratureSearches.AnyAsync(search => search.ResearchRunId == created.ResearchRunId, CancellationToken.None));
            Assert.True(await db.Studies.AnyAsync(study => study.Pmid == "99123456" && study.Doi == "10.1000/medresearch-e2e-sleep-recall", CancellationToken.None));
            Assert.True(await db.ResearchStudyDiscoveries.AnyAsync(discovery => discovery.ResearchRunId == created.ResearchRunId, CancellationToken.None));
            Assert.True(await db.EvidenceExtractions.AnyAsync(extraction => extraction.ResearchRunId == created.ResearchRunId, CancellationToken.None));
            Assert.True(await db.Evidence.AnyAsync(evidence => evidence.ResearchRunId == created.ResearchRunId && evidence.GroundingValidated, CancellationToken.None));
            Assert.True(await db.EvidenceEvaluations.AnyAsync(evaluation => evaluation.ResearchRunId == created.ResearchRunId, CancellationToken.None));
            Assert.True(await db.ResearchReports.AnyAsync(report => report.ResearchRunId == created.ResearchRunId, CancellationToken.None));

            var claimEvidence = await (
                from reportEntity in db.ResearchReports
                join claim in db.ResearchReportClaims on reportEntity.Id equals claim.ResearchReportId
                join link in db.ResearchReportClaimEvidence on claim.Id equals link.ResearchReportClaimId
                join evidence in db.Evidence on link.EvidenceId equals evidence.Id
                join study in db.Studies on evidence.StudyId equals study.Id
                where reportEntity.ResearchRunId == created.ResearchRunId
                select new { claim, link, evidence, study })
                .SingleAsync(CancellationToken.None);

            Assert.Equal(ResearchReportClaimType.Conclusion, claimEvidence.claim.ClaimType);
            Assert.Equal(claimEvidence.evidence.Id, claimEvidence.link.EvidenceId);
            Assert.Equal("99123456", claimEvidence.study.Pmid);
            Assert.Equal("10.1000/medresearch-e2e-sleep-recall", claimEvidence.study.Doi);
        }

        var reportResponse = await client.GetAsync($"/api/research/{created.ResearchRunId}/report");
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<ResearchReportResponse>();
        Assert.NotNull(report);
        Assert.Equal(ResearchReportStatus.Completed.ToString(), report.Status);
        Assert.Equal(question, report.Question);
        Assert.Equal(1, report.Coverage.SearchQueryCount);
        Assert.Contains("PubMed", report.Coverage.SearchedSources);
        var returnedClaim = Assert.Single(report.Claims);
        var returnedCitation = Assert.Single(returnedClaim.Citations);
        Assert.Equal("99123456", returnedCitation.Pmid);
        Assert.Equal("10.1000/medresearch-e2e-sleep-recall", returnedCitation.Doi);
        Assert.Equal("Fake randomized sleep recall trial", returnedCitation.Title);
        Assert.Contains("sleep improved recall", returnedCitation.SupportingText, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            [typeof(ResearchPlanDraft), typeof(EvidenceExtractionDraft), typeof(EvidenceEvaluationDraft), typeof(ResearchReportDraft)],
            fakeLlm.RequestedTypes);
        Assert.Equal(1, fakeLiterature.RequestCount);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }

    private sealed class FakePipelineApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly FakeStructuredLlmClient _fakeLlm;
        private readonly FakeScientificLiteratureSource _fakeLiterature;

        public CapturingLoggerProvider LogProvider { get; } = new();

        public FakePipelineApiFactory(
            string connectionString,
            FakeStructuredLlmClient fakeLlm,
            FakeScientificLiteratureSource fakeLiterature)
        {
            _connectionString = connectionString;
            _fakeLlm = fakeLlm;
            _fakeLiterature = fakeLiterature;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddProvider(LogProvider));
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MedResearch"] = _connectionString,
                    ["ResearchProcessing:Enabled"] = "false",
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["AI:Provider"] = "OpenAI",
                    ["AI:TimeoutSeconds"] = "30",
                    ["AI:MaxOutputTokens"] = "2000"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IStructuredLlmClient>();
                services.RemoveAll<IScientificLiteratureSource>();
                services.RemoveAll<DbContextOptions<MedResearchDbContext>>();
                services.RemoveAll<MedResearchDbContext>();
                services.RemoveAll<IDbContextFactory<MedResearchDbContext>>();
                services.RemoveAll<IResearchRunQueue>();

                services.AddDbContext<MedResearchDbContext>(options =>
                    options.UseNpgsql(
                        _connectionString,
                        npgsql => npgsql.MigrationsAssembly(typeof(MedResearchDbContext).Assembly.FullName)));

                services.AddDbContextFactory<MedResearchDbContext>(
                    options => options.UseNpgsql(
                        _connectionString,
                        npgsql => npgsql.MigrationsAssembly(typeof(MedResearchDbContext).Assembly.FullName)),
                    ServiceLifetime.Scoped);

                services.AddScoped<IResearchRunQueue>(provider =>
                    new PostgreSqlResearchRunQueue(provider.GetRequiredService<IDbContextFactory<MedResearchDbContext>>()));

                services.AddSingleton<IStructuredLlmClient>(_fakeLlm);
                services.AddSingleton<IScientificLiteratureSource>(_fakeLiterature);
            });
        }
    }

    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public string Messages => string.Join(Environment.NewLine, _messages);

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, _messages);
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly ConcurrentQueue<string> _messages;

            public CapturingLogger(string categoryName, ConcurrentQueue<string> messages)
            {
                _categoryName = categoryName;
                _messages = messages;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= LogLevel.Warning;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                _messages.Enqueue($"{logLevel} {_categoryName}: {formatter(state, exception)} {exception}");
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeScientificLiteratureSource : IScientificLiteratureSource
    {
        private const string AbstractText = "In this randomized controlled trial, sleep improved recall in 120 adults compared with wakefulness.";

        public string SourceName => "PubMed";

        public int RequestCount { get; private set; }

        public Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            Assert.Contains("sleep", request.Query, StringComparison.OrdinalIgnoreCase);

            var candidate = new ScientificStudyCandidate(
                "99123456",
                "10.1000/medresearch-e2e-sleep-recall",
                "Fake randomized sleep recall trial",
                AbstractText,
                "Journal of Deterministic Tests",
                new DateOnly(2026, 1, 15),
                2026,
                1,
                15,
                ["Randomized Controlled Trial"],
                ["Ada Lovelace"],
                SourceName);

            return Task.FromResult(new ScientificSearchResult(SourceName, DateTimeOffset.UtcNow, 1, [candidate]));
        }
    }

    private sealed class FakeStructuredLlmClient : IStructuredLlmClient
    {
        public List<Type> RequestedTypes { get; } = [];

        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(
            StructuredLlmRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedTypes.Add(typeof(T));
            object value = typeof(T).Name switch
            {
                nameof(ResearchPlanDraft) => new ResearchPlanDraft(
                    "Does structured sleep improve recall in adults?",
                    "adults",
                    "structured sleep",
                    "wakefulness",
                    ["recall"],
                    ["randomized controlled trial"],
                    ["structured sleep recall adults randomized trial"],
                    []),
                nameof(EvidenceExtractionDraft) => new EvidenceExtractionDraft([
                    new EvidenceFindingDraft(
                        "recall",
                        "Sleep improved recall in adults compared with wakefulness.",
                        "In this randomized controlled trial, sleep improved recall in 120 adults compared with wakefulness.",
                        "Positive",
                        "adults",
                        "sleep",
                        "wakefulness",
                        "randomized controlled trial",
                        120,
                        null,
                        null,
                        null,
                        null,
                        null)
                ]),
                nameof(EvidenceEvaluationDraft) => CreateEvaluationDraft(request),
                nameof(ResearchReportDraft) => CreateReportDraft(request),
                _ => throw new InvalidOperationException($"Unexpected fake LLM request type {typeof(T).FullName}.")
            };

            return Task.FromResult(new StructuredGenerationResult<T>(
                (T)value,
                new StructuredLlmProviderMetadata("FakeLLM", "fake-model", null, DateTimeOffset.UtcNow)));
        }

        private static EvidenceEvaluationDraft CreateEvaluationDraft(StructuredLlmRequest request)
        {
            var researchRunId = RequiredMatch(request.UserPrompt, "researchRunId: ([0-9a-fA-F-]{36})");
            var studyId = RequiredMatch(request.UserPrompt, "studyId: ([0-9a-fA-F-]{36})");

            return new EvidenceEvaluationDraft(
                researchRunId,
                studyId,
                "RandomizedControlledTrial",
                "Favorable",
                "Present",
                "wakefulness",
                "Favorable",
                "InsufficientSource",
                "InsufficientSource",
                "InsufficientSource",
                "Unknown",
                "Direct",
                "Moderate",
                "The supplied abstract reports randomized allocation, 120 adults, and a wakefulness comparator, but abstract-level source detail limits blinding, allocation concealment, and attrition assessment.",
                [],
                []);
        }

        private static ResearchReportDraft CreateReportDraft(StructuredLlmRequest request)
        {
            var evidenceId = RequiredMatch(request.UserPrompt, "EvidenceId: ([0-9a-fA-F-]{36})");

            return new ResearchReportDraft(
                "Completed",
                null,
                "One fake source-grounded study reported improved recall after sleep.",
                "The included evidence says sleep improved recall in 120 adults compared with wakefulness.",
                "No conflicting evidence was present in the supplied corpus.",
                "The evidence is abstract-level and intentionally fake for deterministic orchestration testing.",
                "Within this fake test corpus, structured sleep is positively associated with recall.",
                "Limited",
                [new ResearchReportClaimDraft(
                    "Conclusion",
                    "Positive",
                    "The supplied fake study supports improved recall after sleep compared with wakefulness.",
                    [evidenceId])]);
        }

        private static string RequiredMatch(string text, string pattern)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                throw new InvalidOperationException($"Fake LLM could not find required prompt value matching {pattern}.");
            }

            return match.Groups[1].Value;
        }
    }
}
