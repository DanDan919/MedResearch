using MedResearch.Application.Research;
using MedResearch.Domain;

namespace MedResearch.Application.Tests;

public sealed class ResearchProgressUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ForActiveRunReportsCurrentStageAndActiveLease()
    {
        var runId = Guid.NewGuid();
        var store = new FakeProgressStore(new ResearchRunProgressSnapshot(
            runId,
            "Does progress stay truthful?",
            ResearchRunStatus.Extracting,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(-4),
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(5),
            DateTimeOffset.UtcNow,
            3,
            Metrics(evidenceExtractionCount: 1)));
        var useCase = new GetResearchProgressUseCase(store);

        var result = await useCase.ExecuteAsync(runId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Active", result.Processing.LeaseState);
        Assert.Equal("Current", result.Stages.Single(stage => stage.Stage == "Extracting").State);
        Assert.Equal("Pending", result.Stages.Single(stage => stage.Stage == "Evaluating").State);
    }

    [Fact]
    public async Task ExecuteAsync_ForFailedRunDoesNotInventFailedStage()
    {
        var runId = Guid.NewGuid();
        var store = new FakeProgressStore(new ResearchRunProgressSnapshot(
            runId,
            "Does failed progress avoid fake stage blame?",
            ResearchRunStatus.Failed,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(-4),
            DateTimeOffset.UtcNow,
            "Research processing failed.",
            null,
            null,
            2,
            Metrics(researchPlanCount: 1, literatureSearchCount: 1)));
        var useCase = new GetResearchProgressUseCase(store);

        var result = await useCase.ExecuteAsync(runId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Terminal", result.Processing.LeaseState);
        Assert.DoesNotContain(result.Stages, stage => stage.State == "Failed");
        Assert.Equal("Completed", result.Stages.Single(stage => stage.Stage == "Planning").State);
        Assert.Equal("Pending", result.Stages.Single(stage => stage.Stage == "Evaluating").State);
    }

    [Fact]
    public async Task ExecuteAsync_ForExpiredLeaseReportsRecoverableOperationalState()
    {
        var runId = Guid.NewGuid();
        var store = new FakeProgressStore(new ResearchRunProgressSnapshot(
            runId,
            "Does expired lease show honestly?",
            ResearchRunStatus.Searching,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-9),
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(-2),
            4,
            Metrics(researchPlanCount: 1)));
        var useCase = new GetResearchProgressUseCase(store);

        var result = await useCase.ExecuteAsync(runId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Expired", result.Processing.LeaseState);
        Assert.Equal(4, result.Processing.LeaseVersion);
        Assert.Equal("Current", result.Stages.Single(stage => stage.Stage == "Searching").State);
    }

    private static ResearchRunProgressMetrics Metrics(
        int researchPlanCount = 0,
        int plannedSearchQueryCount = 0,
        int literatureSearchCount = 0,
        int literatureSearchSourceCount = 0,
        int literatureSearchResultCount = 0,
        int discoveryPathCount = 0,
        int distinctDiscoveredStudyCount = 0,
        int currentSourceMaterialCount = 0,
        int structuredFullTextMaterialCount = 0,
        int abstractMaterialCount = 0,
        int evidenceExtractionCount = 0,
        int completedEvidenceExtractionCount = 0,
        int skippedEvidenceExtractionCount = 0,
        int evidenceFindingCount = 0,
        int evidenceEvaluationCount = 0,
        int completedEvidenceEvaluationCount = 0,
        int skippedEvidenceEvaluationCount = 0,
        int researchReportCount = 0,
        int researchReportClaimCount = 0)
    {
        return new ResearchRunProgressMetrics(
            researchPlanCount,
            plannedSearchQueryCount,
            literatureSearchCount,
            literatureSearchSourceCount,
            literatureSearchResultCount,
            discoveryPathCount,
            distinctDiscoveredStudyCount,
            currentSourceMaterialCount,
            structuredFullTextMaterialCount,
            abstractMaterialCount,
            evidenceExtractionCount,
            completedEvidenceExtractionCount,
            skippedEvidenceExtractionCount,
            evidenceFindingCount,
            evidenceEvaluationCount,
            completedEvidenceEvaluationCount,
            skippedEvidenceEvaluationCount,
            researchReportCount,
            researchReportClaimCount);
    }

    private sealed class FakeProgressStore : IResearchProgressStore
    {
        private readonly ResearchRunProgressSnapshot? _snapshot;

        public FakeProgressStore(ResearchRunProgressSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<ResearchRunProgressSnapshot?> FindResearchRunProgressSnapshotAsync(
            Guid researchRunId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshot?.ResearchRunId == researchRunId ? _snapshot : null);
        }
    }
}
