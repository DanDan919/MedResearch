using System.Net.Http.Json;
using MedResearch.Api.Research;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedResearch.IntegrationTests;

public sealed partial class FullFakePipelineTests
{
    // Used only by the isolated web-release harness. Production has no seed endpoint or fake provider.
    public static async Task<Guid> SeedBrowserScenarioAsync(string connectionString)
    {
        using var factory = new FakePipelineApiFactory(connectionString, new FakeStructuredLlmClient(), new FakeScientificLiteratureSource(), "UserA");
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/research", new CreateResearchRequest("Deterministic web release scenario"));
        response.EnsureSuccessStatusCode();
        var run = (await response.Content.ReadFromJsonAsync<CreateResearchResponse>())!;
        using var scope = factory.Services.CreateScope();
        var processed = await scope.ServiceProvider.GetRequiredService<ResearchRunProcessor>()
            .ProcessNextQueuedRunAsync("web-release-seed", TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.True(processed);
        var db = scope.ServiceProvider.GetRequiredService<MedResearchDbContext>();
        var persisted = await db.ResearchRuns.SingleAsync(r => r.Id == run.ResearchRunId);
        Assert.True(persisted.Status == ResearchRunStatus.Completed, persisted.FailureReason);
        // Additional discoveries test read-model/UI scale, not synthetic scientific claims.
        var search = await db.LiteratureSearches.FirstAsync(s => s.ResearchRunId == run.ResearchRunId);
        var secondSearch = new LiteratureSearch(Guid.NewGuid(), run.ResearchRunId, "EuropePmc", "Browser scale provenance fixture", DateTimeOffset.UtcNow, 100, 100, 0, search.ResearchPlanId);
        db.LiteratureSearches.Add(secondSearch);
        var originalStudies = await db.Studies.ToArrayAsync();
        foreach (var study in originalStudies)
            db.ResearchStudyDiscoveries.Add(new ResearchStudyDiscovery(Guid.NewGuid(), run.ResearchRunId, secondSearch.Id, study.Id, "EuropePmc", study.Pmid, DateTimeOffset.UtcNow));
        for (var i = 4; i <= 100; i++)
        {
            var study = new Study(Guid.NewGuid(), $"Browser scale study {i:000} " + new string('x', 180), null, null, null, null, null, "EuropePmc");
            db.Studies.Add(study);
            db.ResearchStudyDiscoveries.Add(new ResearchStudyDiscovery(Guid.NewGuid(), run.ResearchRunId, secondSearch.Id, study.Id, "EuropePmc", null, DateTimeOffset.UtcNow));
        }
        await db.SaveChangesAsync();
        return run.ResearchRunId;
    }
}
