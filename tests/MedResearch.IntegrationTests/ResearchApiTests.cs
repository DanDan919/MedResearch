using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using MedResearch.Api.Research;
using MedResearch.Application.Research;
using MedResearch.Application.Research.Provenance;
using MedResearch.Application.Research.Quantitative;
using MedResearch.Application.Research.Synthesis;
using MedResearch.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;

namespace MedResearch.IntegrationTests;

public sealed class ResearchApiTests
{
    [Theory]
    [InlineData(true, false, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, true, HttpStatusCode.ServiceUnavailable)]
    [InlineData(false, false, HttpStatusCode.InternalServerError)]
    [InlineData(false, true, HttpStatusCode.InternalServerError)]
    public async Task DatabaseFailures_AreOperationalErrorsWithoutPrivateDiagnostics(bool transient, bool wrapped, HttpStatusCode expected)
    {
        using var factory = new ResearchApiFactory();
        var failure = new FakeDatabaseException(transient);
        factory.Store.ReadFailure = wrapped ? new InvalidOperationException("private-wrapper-marker", failure) : failure;
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/research/{Guid.NewGuid()}");
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("private-", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationFailure_RemainsBadRequestRatherThanDatabaseOutage()
    {
        using var factory = new ResearchApiFactory();
        factory.Store.ReadFailure = new InvalidOperationException("Invalid test input");
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/research/{Guid.NewGuid()}")).StatusCode);
    }

    private sealed class FakeDatabaseException(bool transient) : DbException("private-database-marker")
    {
        public override bool IsTransient => transient;
    }
    [Fact]
    public async Task AnonymousResearchEndpoints_AreRejected()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/research", new CreateResearchRequest("question"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/research")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/research/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/research/{Guid.NewGuid()}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/research/{Guid.NewGuid()}/quantitative")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/research/{Guid.NewGuid()}/provenance")).StatusCode);
    }

    [Fact]
    public async Task AllowedCorsPreflight_IsHandledBeforeProtectedEndpointAuthorization()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/research");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:3000", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticatedRequestWithMalformedSubject_IsRejected()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClientFor(new string('x', 201));

        var response = await client.GetAsync("/api/research");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ResearchRun_IsolatedBetweenAuthenticatedSubjects()
    {
        using var factory = new ResearchApiFactory();
        using var ownerClient = factory.CreateClientFor("UserA");
        using var otherClient = factory.CreateClientFor("UserB");

        var created = await ownerClient.PostAsJsonAsync("/api/research", new CreateResearchRequest("User A private question"));
        var createdBody = await created.Content.ReadFromJsonAsync<CreateResearchResponse>();

        var ownerRead = await ownerClient.GetAsync($"/api/research/{createdBody!.ResearchRunId}");
        var otherRead = await otherClient.GetAsync($"/api/research/{createdBody.ResearchRunId}");
        var otherList = await otherClient.GetAsync("/api/research");

        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherList.StatusCode);
        var listBody = await otherList.Content.ReadFromJsonAsync<ResearchRunListResponse>();
        Assert.Empty(listBody!.Items);
        Assert.Equal(0, listBody.TotalCount);
    }

    [Fact]
    public async Task ReportAndQuantitativeEndpoints_HideAnotherSubjectsRun()
    {
        using var factory = new ResearchApiFactory();
        using var otherClient = factory.CreateClientFor("UserB");
        var runId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        factory.Store.Seed(new ResearchRunDetails(
            runId,
            "User B private question",
            ResearchRunStatus.Completed.ToString(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null),
            "UserB");
        factory.ReportStore.Seed(CreateReport(runId, Guid.NewGuid(), ResearchReportStatus.Completed), "UserB");

        var reportResponse = await otherClient.GetAsync($"/api/research/{runId}/report");
        var quantitativeResponse = await otherClient.GetAsync($"/api/research/{runId}/quantitative");

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, quantitativeResponse.StatusCode);

        using var ownerClient = factory.CreateClientFor("UserA");
        var foreignReportResponse = await ownerClient.GetAsync($"/api/research/{runId}/report");
        var foreignQuantitativeResponse = await ownerClient.GetAsync($"/api/research/{runId}/quantitative");

        Assert.Equal(HttpStatusCode.NotFound, foreignReportResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignQuantitativeResponse.StatusCode);
    }

    [Fact]
    public async Task ProvenanceEndpoint_ReturnsPersistedLineageWithoutSourceContent()
    {
        using var factory = new ResearchApiFactory();
        var runId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var studyId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var extractionId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var sourceMaterialId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
        var evidenceId = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");
        var model = new ResearchProvenanceReadModel(
            runId,
            "Does sleep improve recall?",
            ResearchRunStatus.Completed,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new ResearchProvenanceCoverage(1, 2, 2, 1, 1, 1, 1, 1, 1, false),
            [new ResearchPlanProvenance(Guid.NewGuid(), "Does sleep improve recall?", ["sleep recall"], "FakeLLM", "fake-model", "planner-v1", DateTimeOffset.UtcNow)],
            [
                new LiteratureSearchProvenance(Guid.NewGuid(), null, "PubMed", "sleep recall", DateTimeOffset.UtcNow, 1, 1, 0, "SucceededWithResults"),
                new LiteratureSearchProvenance(Guid.NewGuid(), null, "EuropePmc", "sleep recall", DateTimeOffset.UtcNow, 1, 1, 0, "SucceededWithResults")
            ],
            [new StudyProvenance(
                studyId,
                "Sleep and recall",
                "12345678",
                "PMC123456",
                "10.1000/sleep",
                "Journal",
                2026,
                1,
                null,
                ["Journal Article"],
                ["Ada Lovelace"],
                "PubMed",
                [
                    new StudyDiscoveryProvenance(Guid.NewGuid(), Guid.NewGuid(), "PubMed", "12345678", "sleep recall", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                    new StudyDiscoveryProvenance(Guid.NewGuid(), Guid.NewGuid(), "EuropePmc", "MED:12345678", "sleep recall", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                ],
                [new SourceMaterialProvenance(sourceMaterialId, studyId, SourceMaterialType.Abstract, "PubMed", "12345678", "SearchMetadataAbstract", "hash", 1, DateTimeOffset.UtcNow, null, SourceMaterialAccessStatus.Unknown, 42, false, true, ["Abstract"])],
                [new EvidenceExtractionProvenance(extractionId, studyId, sourceMaterialId, EvidenceExtractionStatus.Completed, null, EvidenceSourceScope.Abstract, "FakeLLM", "fake-model", "extract-v1", DateTimeOffset.UtcNow, 1, true)],
                [new EvidenceProvenance(
                    EvidenceId: evidenceId,
                    EvidenceExtractionId: extractionId,
                    Outcome: "recall",
                    ResultSummary: "Recall improved.",
                    SupportingText: "Persisted supporting excerpt.",
                    Direction: EvidenceDirection.Positive,
                    SourceScope: EvidenceSourceScope.Abstract,
                    ExtractedAt: DateTimeOffset.UtcNow,
                    GroundingValidated: true,
                    Population: null,
                    ExposureOrIntervention: null,
                    Comparator: null,
                    StudyDesign: null,
                    SampleSize: null,
                    EffectMeasure: null,
                    EffectValue: null,
                    ConfidenceIntervalLower: null,
                    ConfidenceIntervalUpper: null,
                    ConfidenceLevel: null,
                    ReportedStandardError: null,
                    PValue: null)],
                [new EvidenceEvaluationProvenance(Guid.NewGuid(), studyId, EvidenceEvaluationStatus.Completed, null, EvidenceSourceScope.Abstract, [evidenceId], "FakeLLM", "fake-model", "eval-v1", DateTimeOffset.UtcNow, StudyDesignClassification.Unknown, MethodologicalAssessmentState.Unknown, ComparatorPresence.Unclear, null, MethodologicalAssessmentState.Unknown, MethodologicalAssessmentState.Unknown, MethodologicalAssessmentState.Unknown, MethodologicalAssessmentState.Unknown, MethodologicalAssessmentState.Unknown, DirectnessRating.Unclear, MethodologicalConfidence.InsufficientInformation, "Rationale", [], [], false, false, false, false, false, 0, 0)]
            )],
            [new ResearchReportClaimProvenance(Guid.NewGuid(), Guid.NewGuid(), ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "Recall improved.", 0, [evidenceId])],
            [new QuantitativeContributionProvenance(Guid.NewGuid(), "recall", "Fixed", 0, evidenceId, studyId, extractionId, sourceMaterialId)],
            [new LiteratureProviderAttemptProvenance(Guid.NewGuid(), Guid.NewGuid(), "EuropePmc", "sleep recall", LiteratureProviderAttemptStatus.Failed, null,
                LiteratureProviderFailureCategory.NetworkFailure, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, null)]);
        factory.ProvenanceStore.Seed(model, "UserA");

        using var client = factory.CreateClientFor("UserA");
        var response = await client.GetAsync($"/api/research/{runId}/provenance");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("discoveryPathCount", body);
        Assert.Contains("SucceededWithResults", body);
        Assert.Contains("providerAttempts", body);
        Assert.Contains("NetworkFailure", body);
        Assert.Contains("Persisted supporting excerpt.", body);
        Assert.DoesNotContain("raw source body", body, StringComparison.OrdinalIgnoreCase);

        using var otherClient = factory.CreateClientFor("UserB");
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/research/{runId}/provenance")).StatusCode);
    }

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
    public async Task GetResearchProgress_WithExistingRun_ReturnsObservableProgress()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();
        var runId = Guid.NewGuid();
        factory.Store.Seed(new ResearchRunDetails(
            runId,
            "Does progress endpoint expose persisted state?",
            ResearchRunStatus.Queued.ToString(),
            DateTimeOffset.UtcNow,
            null,
            null,
            null));

        var response = await client.GetAsync($"/api/research/{runId}/progress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResearchRunProgressResponse>();

        Assert.NotNull(body);
        Assert.Equal(runId, body.ResearchRunId);
        Assert.Equal(ResearchRunStatus.Queued.ToString(), body.Status);
        Assert.Equal("None", body.Processing.LeaseState);
        Assert.Contains(body.Stages, stage => stage.Stage == ResearchRunStatus.Queued.ToString() && stage.State == "Current");
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
    public async Task GetResearchProgress_WithUnknownRun_ReturnsNotFound()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/research/{Guid.NewGuid()}/progress");

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
        Assert.Equal("recall", citation.Outcome);
        Assert.Equal("Abstract", citation.SourceScope);
        Assert.NotNull(citation.SourceMaterial);
        Assert.Equal("PubMed", citation.SourceMaterial.Provider);
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
            ? [new ResearchReportClaimReadModel(Guid.NewGuid(), ResearchReportClaimType.Conclusion, ResearchReportClaimDirection.Positive, "Supported conclusion claim.", 0, [CreateCitation(evidenceId)])]
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

    private static ResearchReportCitationReadModel CreateCitation(Guid evidenceId)
    {
        return new ResearchReportCitationReadModel(
            evidenceId,
            Guid.NewGuid(),
            "12345678",
            null,
            "10.1000/authoritative",
            "Authoritative study title",
            "Journal",
            2026,
            1,
            null,
            ["Journal Article"],
            ["Ada Lovelace"],
            "PubMed",
            "recall",
            "Recall improved after sleep.",
            "supporting excerpt",
            EvidenceDirection.Positive,
            EvidenceSourceScope.Abstract,
            true,
            "adults",
            "sleep",
            "wakefulness",
            "controlled trial",
            120,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            new ResearchReportSourceMaterialReadModel(
                Guid.NewGuid(),
                SourceMaterialType.Abstract.ToString(),
                "PubMed",
                "SearchMetadataAbstract",
                1,
                DateTimeOffset.UtcNow,
                SourceMaterialAccessStatus.Unknown.ToString(),
                false,
                ["Abstract"]),
            0);
    }
    private sealed class ResearchApiFactory : WebApplicationFactory<Program>
    {
        public InMemoryResearchStore Store { get; } = new();

        public InMemoryResearchReportStore ReportStore { get; } = new();

        public InMemoryQuantitativeStore QuantitativeStore { get; } = new();

        public InMemoryResearchProvenanceStore ProvenanceStore { get; } = new();

        public new HttpClient CreateClient()
        {
            return CreateClientFor("UserA");
        }

        public HttpClient CreateClientFor(string subject)
        {
            var client = base.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
            return client;
        }

        public HttpClient CreateAnonymousClient()
        {
            return base.CreateClient();
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.Scheme,
                        _ => { });
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IResearchStore>();
                services.RemoveAll<IResearchProgressStore>();
                services.RemoveAll<IResearchReportStore>();
                services.RemoveAll<IQuantitativeSynthesisArtifactStore>();
                services.RemoveAll<IResearchProvenanceStore>();
                services.AddSingleton<IResearchStore>(Store);
                services.AddSingleton<IResearchProgressStore>(Store);
                services.AddSingleton<IResearchReportStore>(ReportStore);
                services.AddSingleton<IQuantitativeSynthesisArtifactStore>(QuantitativeStore);
                services.AddSingleton<IResearchProvenanceStore>(ProvenanceStore);
            });
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public new const string Scheme = "IntegrationTest";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers["X-Test-Subject"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(subject))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("sub", subject)], Scheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }

    private sealed class InMemoryQuantitativeStore : IQuantitativeSynthesisArtifactStore
    {
        public Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> PersistAsync(QuantitativeSynthesisReadiness readiness, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>>([]);
        }

        public Task<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>> FindByResearchRunIdAsync(
            Guid researchRunId,
            string ownerSubjectId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<QuantitativeSynthesisArtifactReadModel>>([]);
        }
    }

    private sealed class InMemoryResearchProvenanceStore : IResearchProvenanceStore
    {
        private readonly ConcurrentDictionary<Guid, (ResearchProvenanceReadModel Model, string Owner)> _models = [];

        public void Seed(ResearchProvenanceReadModel model, string ownerSubjectId)
        {
            _models[model.ResearchRunId] = (model, ownerSubjectId);
        }

        public Task<ResearchProvenanceReadModel?> FindAsync(Guid researchRunId, string ownerSubjectId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _models.TryGetValue(researchRunId, out var value) && value.Owner == ownerSubjectId
                    ? value.Model
                    : null);
        }
    }


    private sealed class InMemoryResearchReportStore : IResearchReportStore
    {
        private readonly ConcurrentDictionary<Guid, ResearchReportReadModel> _reports = [];
        private readonly ConcurrentDictionary<Guid, string> _owners = [];

        public void Seed(ResearchReportReadModel report, string ownerSubjectId = "UserA")
        {
            _reports[report.ResearchRunId] = report;
            _owners[report.ResearchRunId] = ownerSubjectId;
        }

        public Task<bool> HasReportAsync(Guid researchRunId, string promptVersion, CancellationToken cancellationToken)
        {
            return Task.FromResult(_reports.ContainsKey(researchRunId));
        }

        public Task PersistReportAsync(ResearchSynthesisResult result, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<ResearchReportReadModel?> FindReportAsync(Guid researchRunId, string ownerSubjectId, CancellationToken cancellationToken)
        {
            _reports.TryGetValue(researchRunId, out var report);
            if (report is not null && (!_owners.TryGetValue(researchRunId, out var owner) || owner != ownerSubjectId))
            {
                report = null;
            }

            return Task.FromResult(report);
        }
    }
    private sealed class InMemoryResearchStore : IResearchStore
        , IResearchProgressStore
    {
        public Exception? ReadFailure { get; set; }
        private readonly ConcurrentDictionary<Guid, ResearchRunDetails> _runs = [];
        private readonly ConcurrentDictionary<Guid, string> _owners = [];

        public void Seed(ResearchRunDetails details, string ownerSubjectId = "UserA")
        {
            _runs[details.ResearchRunId] = details;
            _owners[details.ResearchRunId] = ownerSubjectId;
        }

        public Task PersistInitialResearchAsync(
            ResearchQuestion question,
            ResearchRun run,
            string ownerSubjectId,
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
            _owners[run.Id] = ownerSubjectId;

            return Task.CompletedTask;
        }

        public Task<ResearchRunDetails?> FindResearchRunAsync(Guid researchRunId, string ownerSubjectId, CancellationToken cancellationToken)
        {
            if (ReadFailure is not null) throw ReadFailure;
            _runs.TryGetValue(researchRunId, out var result);
            if (result is not null && (!_owners.TryGetValue(researchRunId, out var owner) || owner != ownerSubjectId))
            {
                result = null;
            }

            return Task.FromResult(result);
        }

        public Task<ResearchRunListResult> ListResearchRunsAsync(
            int page,
            int pageSize,
            ResearchRunStatus? status,
            string ownerSubjectId,
            CancellationToken cancellationToken)
        {
            var filtered = _runs
                .Where(item => _owners.TryGetValue(item.Key, out var owner) && owner == ownerSubjectId)
                .Select(item => item.Value)
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

        public Task<ResearchRunProgressSnapshot?> FindResearchRunProgressSnapshotAsync(
            Guid researchRunId,
            string ownerSubjectId,
            CancellationToken cancellationToken)
        {
            if (!_runs.TryGetValue(researchRunId, out var run)
                || !_owners.TryGetValue(researchRunId, out var owner)
                || owner != ownerSubjectId)
            {
                return Task.FromResult<ResearchRunProgressSnapshot?>(null);
            }

            return Task.FromResult<ResearchRunProgressSnapshot?>(new ResearchRunProgressSnapshot(
                run.ResearchRunId,
                run.Question,
                Enum.Parse<ResearchRunStatus>(run.Status),
                run.CreatedAt,
                run.StartedAt,
                run.CompletedAt,
                run.FailureReason,
                null,
                null,
                0,
                new ResearchRunProgressMetrics(
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0)));
        }
    }
}
