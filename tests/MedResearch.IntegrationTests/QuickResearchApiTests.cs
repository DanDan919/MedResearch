using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using MedResearch.Application.QuickResearch;
using MedResearch.Domain.QuickResearch;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.QuickResearch;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedResearch.IntegrationTests;

[Collection(QuickResearchPostgreSqlCollection.Name)]
public sealed class QuickResearchApiTests(PostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task FakeQuickRun_CompletesAsynchronously_AndSurvivesApiRestart()
    {
        Skip.IfNot(fixture.IsAvailable, $"Docker-backed PostgreSQL tests skipped: {fixture.UnavailableReason}");
        var subject = Guid.NewGuid().ToString();
        Guid id;
        using (var firstHost = new QuickApiFactory(fixture, processing: true))
        {
            using var owner = firstHost.ForOwner(subject);
            var response = await owner.PostAsJsonAsync("/api/quick-research", new { question = "Synthetic medical question" });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.Private);
            Assert.True(response.Headers.CacheControl.NoStore);
            var accepted = (await response.Content.ReadFromJsonAsync<QuickResearchDetails>())!;
            id = accepted.Id;
            Assert.Equal("Queued", accepted.Status);
            Assert.Equal($"/api/quick-research/{id}", response.Headers.Location!.OriginalString);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            QuickResearchDetails? completed;
            do
            {
                completed = await owner.GetFromJsonAsync<QuickResearchDetails>($"/api/quick-research/{id}");
                if (completed!.Status == "Completed") break;
                await Task.Delay(50);
            } while (DateTimeOffset.UtcNow < deadline);
            Assert.Equal("Completed", completed!.Status);
            Assert.True(completed.Result!.IsSynthetic);
            Assert.Empty(completed.Result.Facts);
        }
        using var restartedHost = new QuickApiFactory(fixture);
        using var sameOwner = restartedHost.ForOwner(subject);
        var persisted = await sameOwner.GetFromJsonAsync<QuickResearchDetails>($"/api/quick-research/{id}");
        Assert.Equal("Completed", persisted!.Status);
        Assert.NotNull(persisted.CompletedAt);
        Assert.True(persisted.Result!.IsSynthetic);
        Assert.Single((await sameOwner.GetFromJsonAsync<QuickResearchHistory>("/api/quick-research"))!.Items);
        using var otherOwner = restartedHost.ForOwner("another-owner");
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.GetAsync($"/api/quick-research/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.GetAsync($"/api/quick-research/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(0, (await otherOwner.GetFromJsonAsync<QuickResearchHistory>("/api/quick-research"))!.TotalCount);
        Assert.DoesNotContain("ownerSubjectId", await sameOwner.GetStringAsync($"/api/quick-research/{id}"));
    }

    [Fact]
    public async Task AnonymousQuickEndpoints_AreUnauthorized()
    {
        using var host = new QuickApiFactory();
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/quick-research")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/quick-research/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/quick-research", new { question = "Question" })).StatusCode);
    }

    [SkippableFact]
    public async Task RetrievalApi_ReturnsOnlyOwnerScopedPublicationReferences_AfterHostRestart()
    {
        Skip.IfNot(fixture.IsAvailable, $"Docker-backed PostgreSQL tests skipped: {fixture.UnavailableReason}");
        var subject = Guid.NewGuid().ToString();
        Guid id;
        using (var host = new QuickApiFactory(fixture, processing: true, retrievalSources: new ApiRetrievalSources()))
        {
            using var owner = host.ForOwner(subject);
            var accepted = await owner.PostAsJsonAsync("/api/quick-research", new { question = "Влияет ли креатин на память?" });
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            id = (await accepted.Content.ReadFromJsonAsync<QuickResearchDetails>())!.Id;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            QuickResearchDetails? result;
            do
            {
                result = await owner.GetFromJsonAsync<QuickResearchDetails>($"/api/quick-research/{id}");
                if (result!.Status == "Completed") break;
                await Task.Delay(50);
            } while (DateTimeOffset.UtcNow < deadline);
            Assert.Equal("Completed", result!.Status);
            Assert.Null(result.Result);
            Assert.Equal("ru", result.Retrieval!.Language);
            Assert.Equal("Selected", result.Retrieval.Outcome);
            Assert.Equal(2, Assert.Single(result.Retrieval.Sources).Discoveries.Length);
        }
        using var restarted = new QuickApiFactory(fixture);
        using var sameOwner = restarted.ForOwner(subject);
        var saved = (await sameOwner.GetFromJsonAsync<QuickResearchDetails>($"/api/quick-research/{id}"))!;
        Assert.Equal("123", Assert.Single(saved.Retrieval!.Sources).Pmid);
        Assert.DoesNotContain("candidates", await sameOwner.GetStringAsync($"/api/quick-research/{id}"));
        using var foreign = restarted.ForOwner("other-retrieval-owner");
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/quick-research/{id}")).StatusCode);
        Assert.Equal(0, (await foreign.GetFromJsonAsync<QuickResearchHistory>("/api/quick-research"))!.TotalCount);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("owner")]
    [InlineData("pagination")]
    public async Task InvalidQuickInput_IsRejectedBeforePersistence(string defect)
    {
        using var host = new QuickApiFactory();
        using var client = host.ForOwner("owner-A");
        var response = defect switch
        {
            "owner" => await client.PostAsJsonAsync("/api/quick-research", new { question = "Question", ownerSubjectId = "owner-B" }),
            "pagination" => await client.GetAsync("/api/quick-research?pageSize=99999"),
            _ => await client.PostAsJsonAsync("/api/quick-research", new { question = " " })
        };
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class QuickApiFactory(PostgreSqlFixture? database = null, bool processing = false, IQuickScientificSources? retrievalSources = null) : WebApplicationFactory<Program>
    {
        public HttpClient ForOwner(string subject)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Q0-Test-Subject", subject);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Q0Test";
                options.DefaultChallengeScheme = "Q0Test";
            }).AddScheme<AuthenticationSchemeOptions, QuickTestAuthentication>("Q0Test", _ => { });
            services.RemoveAll<IHostedService>();
            if (database is null) return;
            services.RemoveAll<MedResearchDbContext>();
            services.RemoveAll<DbContextOptions<MedResearchDbContext>>();
            services.RemoveAll<IDbContextFactory<MedResearchDbContext>>();
            services.AddDbContextFactory<MedResearchDbContext>(options => options.UseNpgsql(database.ConnectionString), ServiceLifetime.Scoped);
            services.RemoveAll<QuickResearchOptions>();
            services.AddSingleton(new QuickResearchOptions { Enabled = processing, PollIntervalSeconds = 1 });
            if (retrievalSources is not null)
            {
                services.RemoveAll<IQuickScientificSources>();
                services.AddSingleton(retrievalSources);
                services.RemoveAll<QuickRetrievalOptions>();
                services.AddSingleton(new QuickRetrievalOptions { Enabled = true });
            }
            if (processing) services.AddHostedService<BackgroundQuickResearchWorker>();
        });
    }

    private sealed class ApiRetrievalSources : IQuickScientificSources
    {
        public string[] EnabledProviders => ["PubMed", "EuropePmc"];
        public string PrepareQuery(string provider, string query) => query;
        public Task<QuickProviderPage> SearchAsync(string provider, Guid runId, Guid attemptId, string query, CancellationToken token) =>
            Task.FromResult(new QuickProviderPage(1, [new("123", null, "10.1234/creatine", "Creatine and memory.", null, null, null, 2024,
                [], [], new(attemptId, provider, "123"))], 0));
    }

    private sealed class QuickTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers["X-Q0-Test-Subject"].FirstOrDefault();
            return Task.FromResult(string.IsNullOrWhiteSpace(subject) ? AuthenticateResult.NoResult() :
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "Q0Test")), "Q0Test")));
        }
    }
}
