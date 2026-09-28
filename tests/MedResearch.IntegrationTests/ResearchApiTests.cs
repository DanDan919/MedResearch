using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using MedResearch.Api.Research;
using MedResearch.Application.Research;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MedResearch.IntegrationTests;

public sealed class ResearchApiTests
{
    [Fact]
    public async Task PostResearch_WithValidRequest_ReturnsCreated()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/research", new CreateResearchRequest(
            "Does chronic sleep deprivation impair working memory in adults?"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadFromJsonAsync<CreateResearchResponse>();

        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.ResearchRunId);
        Assert.Equal(ResearchRunStatus.Queued.ToString(), body.Status);
        Assert.Equal($"/api/research/{body.ResearchRunId}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task PostResearch_WithEmptyQuestion_ReturnsBadRequest()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/research", new CreateResearchRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetResearch_WithExistingRun_ReturnsRunState()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();
        const string question = "Does chronic sleep deprivation impair working memory in adults?";

        var createResponse = await client.PostAsJsonAsync("/api/research", new CreateResearchRequest(question));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateResearchResponse>();

        var getResponse = await client.GetAsync($"/api/research/{created!.ResearchRunId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = await getResponse.Content.ReadFromJsonAsync<ResearchRunResponse>();

        Assert.NotNull(body);
        Assert.Equal(created.ResearchRunId, body.ResearchRunId);
        Assert.Equal(question, body.Question);
        Assert.Equal(ResearchRunStatus.Queued.ToString(), body.Status);
        Assert.Null(body.StartedAt);
        Assert.Null(body.CompletedAt);
        Assert.Null(body.FailureReason);
    }

    [Fact]
    public async Task GetResearchList_ReturnsPagedResearchHistory()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();
        var olderRunId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var newerRunId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        factory.Store.Seed(new ResearchRunDetails(
            olderRunId,
            "Older research question?",
            ResearchRunStatus.Queued.ToString(),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            null,
            null,
            null));
        factory.Store.Seed(new ResearchRunDetails(
            newerRunId,
            "Newer research question?",
            ResearchRunStatus.Completed.ToString(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1),
            null));

        var response = await client.GetAsync("/api/research?page=1&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResearchRunListResponse>();

        Assert.NotNull(body);
        var item = Assert.Single(body.Items);
        Assert.Equal(newerRunId, item.ResearchRunId);
        Assert.Equal("Newer research question?", item.Question);
        Assert.Equal(1, body.Page);
        Assert.Equal(1, body.PageSize);
        Assert.Equal(2, body.TotalCount);
        Assert.Equal(2, body.TotalPages);
    }

    [Fact]
    public async Task GetResearchList_WithStatusFilter_ReturnsFilteredHistory()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();
        factory.Store.Seed(new ResearchRunDetails(Guid.NewGuid(), "Queued?", ResearchRunStatus.Queued.ToString(), DateTimeOffset.UtcNow, null, null, null));
        factory.Store.Seed(new ResearchRunDetails(Guid.NewGuid(), "Completed?", ResearchRunStatus.Completed.ToString(), DateTimeOffset.UtcNow.AddMinutes(1), null, DateTimeOffset.UtcNow.AddMinutes(2), null));

        var response = await client.GetAsync("/api/research?status=Completed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResearchRunListResponse>();

        Assert.NotNull(body);
        var item = Assert.Single(body.Items);
        Assert.Equal(ResearchRunStatus.Completed.ToString(), item.Status);
        Assert.Equal(1, body.TotalCount);
    }

    [Theory]
    [InlineData("/api/research?page=0")]
    [InlineData("/api/research?pageSize=0")]
    [InlineData("/api/research?pageSize=101")]
    [InlineData("/api/research?status=Done")]
    public async Task GetResearchList_WithInvalidQuery_ReturnsBadRequest(string uri)
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetResearch_WithFailedRun_ReturnsFailureState()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var startedAt = createdAt.AddMinutes(1);
        var completedAt = createdAt.AddMinutes(2);

        factory.Store.Seed(new ResearchRunDetails(
            runId,
            "Does deterministic failure handling preserve safe run state?",
            ResearchRunStatus.Failed.ToString(),
            createdAt,
            startedAt,
            completedAt,
            "Research processing failed."));

        var response = await client.GetAsync($"/api/research/{runId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResearchRunResponse>();

        Assert.NotNull(body);
        Assert.Equal(runId, body.ResearchRunId);
        Assert.Equal(ResearchRunStatus.Failed.ToString(), body.Status);
        Assert.Equal("Research processing failed.", body.FailureReason);
        Assert.Equal(completedAt, body.CompletedAt);
    }

    [Fact]
    public async Task GetResearch_WithUnknownRun_ReturnsNotFound()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/research/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }


    [Fact]
    public async Task GetResearchReport_WithCompletedReport_ReturnsReportAndCitationProjection()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        factory.Store.Seed(new ResearchRunDetails(runId, "Does sleep improve recall?", ResearchRunStatus.Completed.ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        factory.ReportStore.Seed(CreateReport(runId, evidenceId, ResearchReportStatus.Completed));

        var response = await client.GetAsync($"/api/research/{runId}/report");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResearchReportResponse>();
        Assert.NotNull(body);
        Assert.Equal(ResearchReportStatus.Completed.ToString(), body.Status);
        var claim = Assert.Single(body.Claims);
        var citation = Assert.Single(claim.Citations);
        Assert.Equal(evidenceId, citation.EvidenceId);
        Assert.Equal("12345678", citation.Pmid);
        Assert.Equal("10.1000/authoritative", citation.Doi);
        Assert.Equal("Authoritative study title", citation.Title);
    }

    [Fact]
    public async Task GetResearchReport_WithKnownRunButNoReport_ReturnsConflict()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        factory.Store.Seed(new ResearchRunDetails(runId, "Does sleep improve recall?", ResearchRunStatus.Synthesizing.ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null));

        var response = await client.GetAsync($"/api/research/{runId}/report");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetResearchReport_WithUnknownRun_ReturnsNotFound()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/research/{Guid.NewGuid()}/report");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetResearchReport_WithInsufficientEvidenceReport_ReturnsExplicitStatus()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        factory.Store.Seed(new ResearchRunDetails(runId, "Does sleep improve recall?", ResearchRunStatus.Completed.ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));
        factory.ReportStore.Seed(CreateReport(runId, Guid.NewGuid(), ResearchReportStatus.InsufficientEvidence));

        var response = await client.GetAsync($"/api/research/{runId}/report");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResearchReportResponse>();
        Assert.NotNull(body);
        Assert.Equal(ResearchReportStatus.InsufficientEvidence.ToString(), body.Status);
        Assert.Equal(ResearchReportInsufficientEvidenceReason.NoValidatedEvidence.ToString(), body.InsufficientEvidenceReason);
        Assert.Empty(body.Claims);
    }

    private static ResearchReportReadModel CreateReport(Guid runId, Guid evidenceId, ResearchReportStatus status)
    {
        var coverage = new ResearchReportCoverageReadModel(1, 1, 1, status == ResearchReportStatus.Completed ? 1 : 0, status == ResearchReportStatus.Completed ? 1 : 0, status == ResearchReportStatus.Completed ? 1 : 0, 1, 0, 1, 0, status == ResearchReportStatus.Completed ? 1 : 0, 0, false, false, true, ["PubMed"]);
        ResearchReportClaimReadModel[] claims = status == ResearchReportStatus.Completed
            ? [new ResearchReportClaimReadModel(Guid.NewGuid(), ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "Supported conclusion claim.", 0, [new ResearchReportCitationReadModel(evidenceId, Guid.NewGuid(), "12345678", null, "10.1000/authoritative", "Authoritative study title", "supporting excerpt", EvidenceDirection.Positive, 0)])]
            : [];

        return new ResearchReportReadModel(
            runId,
            Guid.NewGuid(),
            status,
            status == ResearchReportStatus.InsufficientEvidence ? ResearchReportInsufficientEvidenceReason.NoValidatedEvidence : null,
            "Does sleep improve recall?",
            "Executive summary.",
            "Evidence summary.",
            "Conflict summary.",
            "Limitations summary.",
            "Conclusion.",
            status == ResearchReportStatus.InsufficientEvidence ? SynthesisConfidence.InsufficientEvidence : SynthesisConfidence.Limited,
            ResearchSynthesisPrompt.Version,
            DateTimeOffset.UtcNow,
            coverage,
            ["Abstract-level evidence only."],
            claims);
    }
    private sealed class ResearchApiFactory : WebApplicationFactory<Program>
    {
        public InMemoryResearchStore Store { get; } = new();

        public InMemoryResearchReportStore ReportStore { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IResearchStore>();
                services.RemoveAll<IResearchReportStore>();
                services.AddSingleton<IResearchStore>(Store);
                services.AddSingleton<IResearchReportStore>(ReportStore);
            });
        }
    }


    private sealed class InMemoryResearchReportStore : IResearchReportStore
    {
        private readonly ConcurrentDictionary<Guid, ResearchReportReadModel> _reports = [];

        public void Seed(ResearchReportReadModel report)
        {
            _reports[report.ResearchRunId] = report;
        }

        public Task<bool> HasReportAsync(Guid researchRunId, string promptVersion, CancellationToken cancellationToken)
        {
            return Task.FromResult(_reports.ContainsKey(researchRunId));
        }

        public Task PersistReportAsync(ResearchSynthesisResult result, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<ResearchReportReadModel?> FindReportAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            _reports.TryGetValue(researchRunId, out var report);
            return Task.FromResult(report);
        }
    }
    private sealed class InMemoryResearchStore : IResearchStore
    {
        private readonly ConcurrentDictionary<Guid, ResearchRunDetails> _runs = [];

        public void Seed(ResearchRunDetails details)
        {
            _runs[details.ResearchRunId] = details;
        }

        public Task PersistInitialResearchAsync(
            ResearchQuestion question,
            ResearchRun run,
            CancellationToken cancellationToken)
        {
            _runs[run.Id] = new ResearchRunDetails(
                run.Id,
                question.Text,
                run.Status.ToString(),
                run.CreatedAt,
                run.StartedAt,
                run.CompletedAt,
                run.FailureReason);

            return Task.CompletedTask;
        }

        public Task<ResearchRunDetails?> FindResearchRunAsync(Guid researchRunId, CancellationToken cancellationToken)
        {
            _runs.TryGetValue(researchRunId, out var result);
            return Task.FromResult(result);
        }

        public Task<ResearchRunListResult> ListResearchRunsAsync(
            int page,
            int pageSize,
            ResearchRunStatus? status,
            CancellationToken cancellationToken)
        {
            var filtered = _runs.Values
                .Where(run => status is null || run.Status == status.Value.ToString())
                .OrderByDescending(run => run.CreatedAt)
                .ThenByDescending(run => run.ResearchRunId)
                .ToArray();

            var items = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(run => new ResearchRunSummary(
                    run.ResearchRunId,
                    Guid.NewGuid(),
                    run.Question,
                    run.Status,
                    run.CreatedAt,
                    run.StartedAt,
                    run.CompletedAt,
                    run.FailureReason))
                .ToArray();

            var totalPages = filtered.Length == 0
                ? 0
                : (int)Math.Ceiling(filtered.Length / (double)pageSize);

            return Task.FromResult(new ResearchRunListResult(items, page, pageSize, filtered.Length, totalPages));
        }
    }
}
