using MedResearch.Application.Research.Literature;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class ScientificLiteratureSearchCoordinatorTests
{
    [Fact]
    public async Task SearchAsync_ExecutesEachEnabledSourceAsSeparateSearchExecution()
    {
        var pubMed = new RecordingScientificSource("PubMed", [CreateCandidate("123", null, "10.1000/shared", "PubMed")]);
        var europePmc = new RecordingScientificSource("EuropePmc", [CreateCandidate("123", "PMC123", "https://doi.org/10.1000/shared", "EuropePmc")]);
        var store = new RecordingSearchResultStore();
        var coordinator = CreateCoordinator([pubMed, europePmc], store);
        var runId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        await coordinator.SearchAsync(runId, planId, ["sleep memory"], CancellationToken.None);

        Assert.Single(pubMed.Requests);
        Assert.Single(europePmc.Requests);
        Assert.Equal(2, store.Requests.Count);
        Assert.Equal(["PubMed", "EuropePmc"], store.Requests.Select(request => request.Source).ToArray());
        Assert.NotEqual(store.Requests[0].SearchExecutionId, store.Requests[1].SearchExecutionId);
        Assert.All(store.Requests, request =>
        {
            Assert.Equal(runId, request.ResearchRunId);
            Assert.Equal(planId, request.ResearchPlanId);
            Assert.Equal("sleep memory", request.Query);
        });
    }

    [Fact]
    public async Task SearchAsync_PersistsSuccessfulSourceWhenAnotherSourceFails()
    {
        var failing = new RecordingScientificSource("PubMed", [], new ScientificLiteratureSourceException("provider failed"));
        var successful = new RecordingScientificSource("EuropePmc", [CreateCandidate("123", "PMC123", "10.1000/shared", "EuropePmc")]);
        var store = new RecordingSearchResultStore();
        var coordinator = CreateCoordinator([failing, successful], store);

        await coordinator.SearchAsync(Guid.NewGuid(), Guid.NewGuid(), ["sleep memory"], CancellationToken.None);

        var request = Assert.Single(store.Requests);
        Assert.Equal("EuropePmc", request.Source);
        Assert.Single(request.Candidates);
    }

    [Fact]
    public async Task SearchAsync_ThrowsWhenAllSourcesFail()
    {
        var coordinator = CreateCoordinator([
            new RecordingScientificSource("PubMed", [], new ScientificLiteratureSourceException("pubmed failed")),
            new RecordingScientificSource("EuropePmc", [], new ScientificLiteratureSourceException("europe pmc failed"))],
            new RecordingSearchResultStore());

        await Assert.ThrowsAsync<ScientificLiteratureSourceException>(() =>
            coordinator.SearchAsync(Guid.NewGuid(), Guid.NewGuid(), ["sleep memory"], CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_ZeroResultsCountAsSuccessfulSearchExecution()
    {
        var source = new RecordingScientificSource("EuropePmc", []);
        var store = new RecordingSearchResultStore();
        var coordinator = CreateCoordinator([source], store);

        await coordinator.SearchAsync(Guid.NewGuid(), Guid.NewGuid(), ["rare topic"], CancellationToken.None);

        var request = Assert.Single(store.Requests);
        Assert.Equal(0, request.ResultCount);
        Assert.Empty(request.Candidates);
    }

    private static ScientificLiteratureSearchCoordinator CreateCoordinator(
        IReadOnlyCollection<IScientificLiteratureSource> sources,
        IScientificSearchResultStore store)
    {
        return new ScientificLiteratureSearchCoordinator(sources, store, NullLogger<ScientificLiteratureSearchCoordinator>.Instance);
    }

    private static ScientificStudyCandidate CreateCandidate(string? pmid, string? pmcid, string? doi, string source)
    {
        return new ScientificStudyCandidate(
            pmid,
            pmcid,
            doi,
            $"{source} candidate",
            "Reported abstract.",
            "Journal",
            new DateOnly(2026, 1, 1),
            2026,
            1,
            1,
            ["Journal Article"],
            ["Ada Lovelace"],
            pmid ?? pmcid ?? doi,
            source);
    }

    private sealed class RecordingScientificSource : IScientificLiteratureSource
    {
        private readonly IReadOnlyCollection<ScientificStudyCandidate> _candidates;
        private readonly Exception? _exception;

        public RecordingScientificSource(string sourceName, IReadOnlyCollection<ScientificStudyCandidate> candidates, Exception? exception = null)
        {
            SourceName = sourceName;
            _candidates = candidates;
            _exception = exception;
        }

        public string SourceName { get; }

        public List<ScientificSearchRequest> Requests { get; } = [];

        public Task<ScientificSearchResult> SearchAsync(ScientificSearchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new ScientificSearchResult(SourceName, DateTimeOffset.UtcNow, _candidates.Count, _candidates));
        }
    }

    private sealed class RecordingSearchResultStore : IScientificSearchResultStore
    {
        public List<ScientificSearchPersistenceRequest> Requests { get; } = [];

        public Task<ScientificSearchPersistenceResult> PersistSearchResultsAsync(
            ScientificSearchPersistenceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(new ScientificSearchPersistenceResult(request.SearchExecutionId, request.Candidates.Count, 0));
        }
    }
}
