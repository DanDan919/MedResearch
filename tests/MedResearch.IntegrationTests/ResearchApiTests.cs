using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using MedResearch.Api.Research;
using MedResearch.Application.Research;
using MedResearch.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public async Task GetResearch_WithUnknownRun_ReturnsNotFound()
    {
        using var factory = new ResearchApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/research/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class ResearchApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IResearchStore>();
                services.AddSingleton<IResearchStore, InMemoryResearchStore>();
            });
        }
    }

    private sealed class InMemoryResearchStore : IResearchStore
    {
        private readonly ConcurrentDictionary<Guid, ResearchRunDetails> _runs = [];

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
    }
}
