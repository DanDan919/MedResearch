using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedResearch.Api.Research;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Trait("Category", "PostgreSql")]
public sealed class ResearchAdmissionTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    [SkippableFact]
    public async Task SameOwner_TenParallelCreates_AdmitOneWithoutOrphans()
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(index => CreateAsync(client, Guid.NewGuid(), $"Bounded research question {index}")));

        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ResearchRuns.CountAsync());
        Assert.Equal(1, await db.ResearchQuestions.CountAsync());
    }

    [SkippableFact]
    public async Task MultipleApiInstances_ParallelOwners_AdmitTwoGlobally()
    {
        await ResetAsync();
        using var first = new JwtApiFactory(fixture.ConnectionString);
        using var second = new JwtApiFactory(fixture.ConnectionString);
        using var a = Client(first, "owner-a");
        using var b = Client(second, "owner-b");
        using var c = Client(first, "owner-c");
        using var d = Client(second, "owner-d");
        var responses = await Task.WhenAll(new[] { a, b, c, d }
            .Select(client => CreateAsync(client, Guid.NewGuid(), "Global capacity research question")));

        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.ResearchRuns.CountAsync());
        Assert.Equal(2, await db.ResearchQuestions.CountAsync());
    }

    [SkippableFact]
    public async Task LostCreateResponse_RetrySameOwnerKeyBody_ReturnsSameRun()
    {
        await ResetAsync();
        using var factory = new JwtApiFactory(fixture.ConnectionString);
        using var client = Client(factory, "owner");
        var key = Guid.NewGuid();
        using var first = await CreateAsync(client, key, "Idempotent scientific question");
        using var retry = await CreateAsync(client, key, "Idempotent scientific question");
        var original = await first.Content.ReadFromJsonAsync<CreateResearchResponse>();
        var replay = await retry.Content.ReadFromJsonAsync<CreateResearchResponse>();

        Assert.NotNull(original);
        Assert.NotNull(replay);
        Assert.Equal(original.ResearchRunId, replay.ResearchRunId);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ResearchRuns.CountAsync());
        Assert.Equal(1, await db.ResearchQuestions.CountAsync());
    }

    private async Task ResetAsync()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE research_questions CASCADE");
    }

    private static HttpClient Client(JwtApiFactory factory, string owner)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("valid", owner));
        return client;
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid key, string question)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/research")
        {
            Content = JsonContent.Create(new CreateResearchRequest(question))
        };
        request.Headers.Add("Idempotency-Key", key.ToString("D"));
        return client.SendAsync(request);
    }
}
