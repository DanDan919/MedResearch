using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Domain.QuickResearch;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.Application.Tests;

public sealed class QuickRetrievalTests
{
    [Theory]
    [InlineData("Does magnesium help insomnia in adults?", "Помогает ли магний при бессоннице у взрослых?")]
    [InlineData("Does creatine affect memory?", "Влияет ли креатин на память?")]
    [InlineData("How harmful are electronic cigarettes?", "Насколько вредны электронные сигареты?")]
    [InlineData("Can poor sleep increase anxiety?", "Может ли плохой сон повышать тревожность?")]
    [InlineData("Do antidepressants actually work?", "Действительно ли антидепрессанты работают?")]
    [InlineData("What are the long term effects of stimulants?", "Какие долгосрочные эффекты стимуляторов?")]
    public void EnglishAndSupportedRussian_PrepareSameBoundedQuery(string english, string russian)
    {
        var first = QuickQueryPreparation.Prepare(english);
        var second = QuickQueryPreparation.Prepare(russian);
        Assert.Equal(Assert.Single(first.Queries), Assert.Single(second.Queries));
        Assert.Equal("en", first.Language);
        Assert.Equal("ru", second.Language);
        Assert.True(first.Queries[0].Length <= 512);
    }

    [Theory]
    [InlineData("Не влияет ли магний на сон?")]
    [InlineData("Помогает ли неизвестноелекарство?")]
    [InlineData("Magnesium without sleep")]
    [InlineData("Magnesium in adults and children")]
    [InlineData("Adults")]
    public void AmbiguousOrUnsupportedQuestion_DoesNotGuessAQuery(string question) => Assert.Empty(QuickQueryPreparation.Prepare(question).Queries);

    [Fact]
    public void QuestionOrderAndPunctuation_DoNotChangePreparedQuery() =>
        Assert.Equal(QuickQueryPreparation.Prepare("Magnesium, insomnia: adults?").Queries, QuickQueryPreparation.Prepare("ADULTS insomnia magnesium").Queries);

    [Fact]
    public void SameIdentifiers_DeduplicateAndRetainBothDiscoveryPaths()
    {
        var first = Candidate("1");
        var second = first with { Pmcid = "PMC1", Discovery = new(Guid.NewGuid(), "EuropePmc", "MED:1") };
        var source = Assert.Single(Select([first, second]));
        Assert.Equal("1", source.Pmid);
        Assert.Equal("PMC1", source.Pmcid);
        Assert.Equal(2, source.Discoveries.Length);
        Assert.Equal("https://pubmed.ncbi.nlm.nih.gov/1/", source.Url);
    }

    [Theory]
    [InlineData("doi")]
    [InlineData("pmid")]
    [InlineData("bridge")]
    public void IdentifierConflicts_RejectTheWholeAmbiguousComponent(string kind)
    {
        var a = Candidate("1") with { Doi = "10.1234/a" };
        var b = kind == "doi" ? a with { Doi = "10.1234/b" } : Candidate("2") with { Doi = "10.1234/a" };
        var candidates = kind == "bridge" ? new[] { a, Candidate("2") with { Doi = "10.1234/b" }, a with { Doi = "10.1234/b" } } : [a, b];
        Assert.Empty(Select(candidates));
    }

    [Fact]
    public void SimilarTitles_DoNotMergeDifferentPublicationIds() => Assert.Equal(2, Select([Candidate("1"), Candidate("2")]).Length);

    [Theory]
    [InlineData("Magnesium for insomnia in children.")]
    [InlineData("Magnesium for insomnia in adults and children.")]
    [InlineData("Magnesium for insomnia.")]
    public void ExplicitPopulation_RejectsMismatchMixedAndUnknownPopulation(string title) =>
        Assert.Empty(Select([Candidate("1") with { Title = title, Abstract = null }]));

    [Fact]
    public void MissingAbstract_IsNotInvented_AndKnownYearIsNotJanuaryFirst()
    {
        var source = Assert.Single(Select([Candidate("1") with { Abstract = null, PublicationYear = 2024 }]));
        Assert.Null(source.Abstract);
        Assert.Equal(2024, source.PublicationYear);
        Assert.Null(source.PublicationDate);
    }

    [Fact]
    public void Metadata_EnrichesNullButDoesNotSilentlyChooseAConflictingAbstract()
    {
        var rich = Candidate("1") with { Abstract = "Reported source abstract", Journal = "Reported journal" };
        var poor = rich with { Abstract = null, Journal = null };
        Assert.Equal(rich.Abstract, Assert.Single(Select([poor, rich])).Abstract);
        var conflict = rich with { Abstract = "Different source abstract" };
        var source = Assert.Single(Select([rich, conflict]));
        Assert.Null(source.Abstract);
        Assert.Contains("Abstract", source.MetadataConflicts);
    }

    [Fact]
    public void ConflictingYear_DoesNotKeepAFullDateWithContradictoryPrecision()
    {
        var dated = Candidate("1") with { PublicationDate = new DateOnly(2024, 2, 3), PublicationYear = null };
        var conflictingYear = dated with { PublicationDate = null, PublicationYear = 2025 };
        var source = Assert.Single(Select([dated, conflictingYear]));
        Assert.Null(source.PublicationDate);
        Assert.Null(source.PublicationYear);
        Assert.Contains("PublicationDate", source.MetadataConflicts);
        Assert.Contains("PublicationYear", source.MetadataConflicts);
    }

    [Fact]
    public void Selection_IsCappedAndIndependentOfProviderResponseOrder()
    {
        var id = Guid.NewGuid();
        var candidates = Enumerable.Range(1, 8).Select(i => Candidate(i.ToString())).ToArray();
        var selector = new QuickPublicationSelector();
        var a = selector.Select(id, Plan(), candidates).Sources;
        var b = selector.Select(id, Plan(), candidates.Reverse().ToArray()).Sources;
        Assert.Equal(5, a.Length);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        Assert.NotEqual(a[0].Id, selector.Select(Guid.NewGuid(), Plan(), candidates).Sources[0].Id);
    }

    [Theory]
    [InlineData("zero", "ZeroResults")]
    [InlineData("irrelevant", "NoRelevantSources")]
    [InlineData("partial", "Partial")]
    [InlineData("failed", "Failed")]
    [InlineData("timeout", "Partial")]
    public async Task Outcomes_AreDistinctAndPersistSafeProviderProvenance(string mode, string expected)
    {
        var store = new MemoryStore();
        var gateway = new FakeSources(mode);
        await Processor(store, gateway).ProcessAsync(Claim(), CancellationToken.None);
        Assert.Equal(expected, store.Last!.Outcome.ToString());
        Assert.True(store.Last.IsFinal);
        Assert.Equal(2, store.Last.Attempts.Length);
        Assert.Empty(store.Last.Candidates);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(store.Last));
        Assert.All(gateway.Calls, c => Assert.Equal(ClaimQuestion, c.Query));
        if (mode == "timeout") Assert.Contains(store.Last.Attempts, a => a.Outcome == QuickSearchOutcome.TimedOut);
        if (mode == "zero") Assert.All(store.Last.Attempts, a => Assert.Equal(0, a.ReturnedCount));
    }

    [Fact]
    public async Task AbandonedAttempt_IsNotRequestedAgain_AndHasUnknownOutcome()
    {
        var claim = Claim();
        var attempt = new QuickSearchAttempt(Guid.NewGuid(), claim.RunId, "PubMed", ClaimQuestion, ClaimQuestion, DateTimeOffset.UtcNow,
            null, QuickSearchOutcome.Started, null, null);
        claim = claim with { Retrieval = new(claim.RunId, Plan(), [attempt], [], [], QuickRetrievalOutcome.Searching, false, 0, []) };
        var store = new MemoryStore();
        var gateway = new FakeSources("normal");
        await Processor(store, gateway).ProcessAsync(claim, CancellationToken.None);
        Assert.Equal("EuropePmc", Assert.Single(gateway.Calls).Provider);
        Assert.Contains(store.Last!.Attempts, a => a.Outcome == QuickSearchOutcome.OutcomeUnknown);
        Assert.Equal(QuickRetrievalOutcome.Partial, store.Last.Outcome);
    }

    [Fact]
    public async Task HostCancellation_LeavesStartedAttemptWithoutPretendingProviderFailed()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new MemoryStore();
        var gateway = new FakeSources("normal") { BeforeSearch = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(store, gateway).ProcessAsync(Claim(), cancellation.Token));
        Assert.Equal(QuickSearchOutcome.Started, Assert.Single(store.Last!.Attempts).Outcome);
        Assert.False(store.Last.IsFinal);
    }

    [Fact]
    public async Task LostLease_BlocksEvenTheFirstExternalRequest()
    {
        var gateway = new FakeSources("normal");
        await Assert.ThrowsAsync<QuickResearchLeaseLostException>(() => Processor(new MemoryStore { AllowWrites = false }, gateway).ProcessAsync(Claim(), CancellationToken.None));
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task UnsupportedRussian_FinishesAsClarification_WithoutRequests()
    {
        var store = new MemoryStore();
        var gateway = new FakeSources("normal");
        await Processor(store, gateway).ProcessAsync(Claim() with { Question = "Неизвестноелекарство" }, CancellationToken.None);
        Assert.Equal(QuickRetrievalOutcome.NeedsClarification, store.Last!.Outcome);
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task DisabledRetrieval_NeverCallsSources_EvenWhenInvokedDirectly()
    {
        var options = new QuickRetrievalOptions();
        Assert.False(options.Enabled);
        var gateway = new FakeSources("normal");
        var store = new MemoryStore();
        await new QuickRetrievalProcessor(store, gateway, new QuickPublicationSelector(), options,
            NullLogger<QuickRetrievalProcessor>.Instance).ProcessAsync(Claim(), CancellationToken.None);
        Assert.Empty(gateway.Calls);
        Assert.All(store.Last!.Attempts, a => Assert.Equal(QuickSearchOutcome.Disabled, a.Outcome));
        Assert.Equal(QuickRetrievalOutcome.Failed, store.Last.Outcome);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void InvalidSearchTimeout_IsRejected(int timeout) => Assert.Throws<InvalidOperationException>(() =>
        new QuickRetrievalOptions { SearchTimeoutSeconds = timeout }.Validate());

    [Theory]
    [InlineData("run")]
    [InlineData("source")]
    [InlineData("attempt")]
    [InlineData("discovery")]
    [InlineData("identifier")]
    public async Task SnapshotValidation_RejectsWrongRunOrForgedLineage(string defect)
    {
        var claim = Claim();
        var store = new MemoryStore();
        await Processor(store, new FakeSources("normal")).ProcessAsync(claim, CancellationToken.None);
        var snapshot = store.Last!;
        snapshot = defect switch
        {
            "run" => snapshot with { QuickRunId = Guid.NewGuid() },
            "source" => snapshot with { Sources = [snapshot.Sources[0] with { QuickRunId = Guid.NewGuid() }] },
            "attempt" => snapshot with { Attempts = snapshot.Attempts.Select(a => a with { QuickRunId = Guid.NewGuid() }).ToArray() },
            "discovery" => snapshot with { Sources = [snapshot.Sources[0] with { Discoveries = [new(Guid.NewGuid(), "PubMed", "1")] }] },
            _ => snapshot with { Sources = [snapshot.Sources[0] with { Pmid = "../private", Doi = null, Pmcid = null }] }
        };
        Assert.Throws<ArgumentException>(() => snapshot.Validate(claim.RunId));
    }

    private const string ClaimQuestion = "\"insomnia\" AND \"magnesium\" AND (\"adult\" OR \"adults\")";
    private static QuickQueryPlan Plan() => QuickQueryPreparation.Prepare("Magnesium insomnia adults");
    private static QuickResearchClaim Claim() => new(Guid.NewGuid(), "worker", 1, "Magnesium insomnia adults");
    private static QuickCandidate Candidate(string pmid) => new(pmid, null, null, "Magnesium for insomnia in adults.", null, null, null, null, [], [], new(Guid.NewGuid(), "PubMed", pmid));
    private static QuickPublication[] Select(QuickCandidate[] values) => new QuickPublicationSelector().Select(Guid.NewGuid(), Plan(), values).Sources;
    private static QuickRetrievalProcessor Processor(MemoryStore store, FakeSources sources) => new(store, sources, new QuickPublicationSelector(),
        new QuickRetrievalOptions { Enabled = true }, NullLogger<QuickRetrievalProcessor>.Instance);

    private sealed class FakeSources(string mode) : IQuickScientificSources
    {
        public string[] EnabledProviders => ["PubMed", "EuropePmc"];
        public List<(string Provider, string Query)> Calls { get; } = [];
        public Action? BeforeSearch { get; init; }
        public string PrepareQuery(string provider, string query) => query;
        public Task<QuickProviderPage> SearchAsync(string provider, Guid runId, Guid attemptId, string query, CancellationToken token)
        {
            Calls.Add((provider, query));
            BeforeSearch?.Invoke();
            token.ThrowIfCancellationRequested();
            if (mode == "failed" || mode == "partial" && provider == "PubMed") throw new ScientificLiteratureSourceException("secret", LiteratureProviderFailureCategory.NetworkFailure);
            if (mode == "timeout" && provider == "PubMed") throw new OperationCanceledException();
            var candidate = Candidate("1") with { Discovery = new(attemptId, provider, "1") };
            if (mode == "irrelevant") candidate = candidate with { Title = "Unrelated article" };
            return Task.FromResult(mode == "zero" ? new QuickProviderPage(0, [], 0) : new QuickProviderPage(1, [candidate], 0));
        }
    }
    private sealed class MemoryStore : IQuickResearchStore
    {
        public bool AllowWrites { get; init; } = true;
        public QuickRetrievalSnapshot? Last { get; private set; }
        public Task<bool> SaveRetrievalAsync(QuickResearchClaim claim, QuickRetrievalSnapshot snapshot, bool settle, CancellationToken token)
        { snapshot.Validate(claim.RunId); Last = snapshot; return Task.FromResult(AllowWrites); }
        public Task<QuickResearchDetails> CreateAsync(QuickResearchRun run, Guid key, CancellationToken token) => throw new NotSupportedException();
        public Task<QuickResearchDetails?> FindAsync(Guid runId, string owner, CancellationToken token) => throw new NotSupportedException();
        public Task<QuickResearchHistory> ListAsync(string owner, int page, int pageSize, CancellationToken token) => throw new NotSupportedException();
        public Task<QuickResearchClaim?> ClaimAsync(string workerId, TimeSpan leaseDuration, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> CompleteAsync(QuickResearchClaim claim, QuickResearchResult result, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> FailAsync(QuickResearchClaim claim, CancellationToken token) => throw new NotSupportedException();
    }
}
