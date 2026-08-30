using MedResearch.Application.Research.Literature;
using MedResearch.Domain;
using MedResearch.Infrastructure.Literature.Persistence;
using MedResearch.Infrastructure.Planning.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ResearchPlanPersistenceTests
{
    private readonly PostgreSqlFixture _fixture;

    public ResearchPlanPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task SaveResearchPlanAsync_PersistsPlanWithQuestionRunAndProvenance()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedResearchRunAsync("Does chronic sleep deprivation impair working memory in adults?");
        var plan = CreatePlan(seed.RunId, seed.QuestionId, seed.QuestionText, ["sleep query one", "sleep query two"]);

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfResearchPlanStore(context);
            await store.SaveResearchPlanAsync(plan, CancellationToken.None);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var reloaded = await verificationContext.ResearchPlans.SingleAsync(
            candidate => candidate.ResearchRunId == seed.RunId,
            CancellationToken.None);

        Assert.Equal(seed.RunId, reloaded.ResearchRunId);
        Assert.Equal(seed.QuestionId, reloaded.ResearchQuestionId);
        Assert.Equal(seed.QuestionText, reloaded.OriginalQuestion);
        Assert.Equal("adults", reloaded.Population);
        Assert.Equal("chronic sleep deprivation", reloaded.ExposureOrIntervention);
        Assert.Equal("adequate sleep", reloaded.Comparator);
        Assert.Equal(["working memory"], reloaded.Outcomes);
        Assert.Equal(["observational study"], reloaded.PreferredStudyTypes);
        Assert.Equal(["sleep query one", "sleep query two"], reloaded.SearchQueries);
        Assert.Equal(["animal studies"], reloaded.ExclusionHints);
        Assert.Equal("OpenAI", reloaded.Provider);
        Assert.Equal("configured-model", reloaded.Model);
        Assert.Equal("research-planner-v1", reloaded.PromptVersion);
    }

    [SkippableFact]
    public async Task LiteratureSearches_CanReferenceQueriesOriginatingFromResearchPlan()
    {
        SkipIfPostgreSqlUnavailable();

        var seed = await SeedResearchRunAsync("Does chronic sleep deprivation impair working memory in adults?");
        var plan = CreatePlan(seed.RunId, seed.QuestionId, seed.QuestionText, ["sleep query one", "sleep query two"]);

        await using (var context = _fixture.CreateDbContext())
        {
            context.ResearchPlans.Add(plan);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = _fixture.CreateDbContext())
        {
            var store = new EfScientificSearchResultStore(context);
            await store.PersistSearchResultsAsync(
                CreateSearchRequest(Guid.NewGuid(), seed.RunId, plan.Id, "sleep query one"),
                CancellationToken.None);
            await store.PersistSearchResultsAsync(
                CreateSearchRequest(Guid.NewGuid(), seed.RunId, plan.Id, "sleep query two"),
                CancellationToken.None);
        }

        await using var verificationContext = _fixture.CreateDbContext();
        var searches = await verificationContext.LiteratureSearches
            .Where(search => search.ResearchPlanId == plan.Id)
            .OrderBy(search => search.Query)
            .ToArrayAsync(CancellationToken.None);

        Assert.Equal(2, searches.Length);
        Assert.Equal(["sleep query one", "sleep query two"], searches.Select(search => search.Query).ToArray());
        Assert.All(searches, search => Assert.Equal(seed.RunId, search.ResearchRunId));
    }

    private async Task<SeededRun> SeedResearchRunAsync(string questionText)
    {
        await using var context = _fixture.CreateDbContext();
        var question = new ResearchQuestion(questionText, DateTimeOffset.UtcNow);
        var run = new ResearchRun(question.Id, question.CreatedAt);

        context.ResearchQuestions.Add(question);
        context.ResearchRuns.Add(run);
        await context.SaveChangesAsync(CancellationToken.None);

        return new SeededRun(question.Id, run.Id, question.Text);
    }

    private static ResearchPlan CreatePlan(Guid runId, Guid questionId, string questionText, string[] searchQueries)
    {
        return new ResearchPlan(
            Guid.NewGuid(),
            runId,
            questionId,
            questionText,
            "adults",
            "chronic sleep deprivation",
            "adequate sleep",
            ["working memory"],
            ["observational study"],
            searchQueries,
            ["animal studies"],
            "OpenAI",
            "configured-model",
            "research-planner-v1",
            DateTimeOffset.UtcNow);
    }

    private static ScientificSearchPersistenceRequest CreateSearchRequest(
        Guid searchExecutionId,
        Guid runId,
        Guid planId,
        string query)
    {
        return new ScientificSearchPersistenceRequest(
            searchExecutionId,
            runId,
            planId,
            "PubMed",
            query,
            DateTimeOffset.UtcNow,
            0,
            []);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }

    private sealed record SeededRun(Guid QuestionId, Guid RunId, string QuestionText);
}
