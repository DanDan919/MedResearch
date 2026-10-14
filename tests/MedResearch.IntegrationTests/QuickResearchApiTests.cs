using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using MedResearch.Application.QuickResearch;
using MedResearch.Application.Research.Ai;
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
    public async Task InvalidOrMissingKey_IsRejectedBeforePersistence()
    {
        using var host = new QuickApiFactory();
        foreach (var key in new[] { "", "bad", Guid.Empty.ToString(), Guid.NewGuid().ToString("N") })
        {
            using var client = host.ForOwner("q41-owner");
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            if (key.Length != 0) client.DefaultRequestHeaders.Add("Idempotency-Key", key);
            var response = await client.PostAsJsonAsync("/api/quick-research", new { question = "Valid question" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("admission-invalid-key", await response.Content.ReadAsStringAsync());
        }
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

    [SkippableFact]
    public async Task AnswerApi_PersistsValidatedAnswer_AfterHostRestart_WithoutGenerationMetadata()
    {
        Skip.IfNot(fixture.IsAvailable, $"Docker-backed PostgreSQL tests skipped: {fixture.UnavailableReason}");
        var ownerId = Guid.NewGuid().ToString();
        var llm = new ApiAnswerLlm();
        Guid id;
        using (var host = new QuickApiFactory(fixture, processing: true, retrievalSources: new ApiRetrievalSources(true), answerClient: llm))
        {
            using var owner = host.ForOwner(ownerId);
            var accepted = await owner.PostAsJsonAsync("/api/quick-research", new { question = "Влияет ли креатин на память?" });
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            id = (await accepted.Content.ReadFromJsonAsync<QuickResearchDetails>())!.Id;
            QuickResearchDetails? result = null;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                result = await owner.GetFromJsonAsync<QuickResearchDetails>($"/api/quick-research/{id}");
                if (result!.Status is "Completed" or "Failed") break;
                await Task.Delay(50);
            }
            Assert.Equal("Completed", result!.Status);
            Assert.Equal("Succeeded", result.AnswerStatus);
            Assert.NotNull(result.Answer);
            Assert.Equal(Assert.Single(result.Retrieval!.Sources).Id, Assert.Single(result.Answer.SourceReferences));
            Assert.Equal(2, result.Retrieval.Sources[0].Discoveries.Length);
            Assert.Equal(1, llm.Calls);
        }
        using var restarted = new QuickApiFactory(fixture);
        using var sameOwner = restarted.ForOwner(ownerId);
        var response = await sameOwner.GetAsync($"/api/quick-research/{id}");
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        var saved = (await response.Content.ReadFromJsonAsync<QuickResearchDetails>())!;
        Assert.Equal("Succeeded", saved.AnswerStatus);
        Assert.NotEmpty(saved.Answer!.Findings);
        var json = await sameOwner.GetStringAsync($"/api/quick-research/{id}");
        Assert.DoesNotContain("inputFingerprint", json);
        Assert.DoesNotContain("promptVersion", json);
        Assert.DoesNotContain("not-public-model", json);
        using var foreign = restarted.ForOwner("another-Q2-owner");
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

    internal sealed class QuickApiFactory(PostgreSqlFixture? database = null, bool processing = false,
        IQuickScientificSources? retrievalSources = null, IStructuredLlmClient? answerClient = null) : WebApplicationFactory<Program>
    {
        public HttpClient ForOwner(string subject, Guid? key = null)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Q0-Test-Subject", subject);
            client.DefaultRequestHeaders.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
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
            services.AddSingleton(new QuickResearchOptions { Enabled = processing, PollIntervalSeconds = 1, LeaseDurationSeconds = answerClient is null ? 60 : 90 });
            if (retrievalSources is not null)
            {
                services.RemoveAll<IQuickScientificSources>();
                services.AddSingleton(retrievalSources);
                services.RemoveAll<QuickRetrievalOptions>();
                services.AddSingleton(new QuickRetrievalOptions { Enabled = true });
            }
            if (answerClient is not null)
            {
                services.RemoveAll<IStructuredLlmClient>();
                services.AddSingleton(answerClient);
                services.RemoveAll<QuickAnswerOptions>();
                services.AddSingleton(new QuickAnswerOptions { Enabled = true });
            }
            if (processing) services.AddHostedService<BackgroundQuickResearchWorker>();
        });
    }

    private sealed class ApiRetrievalSources(bool includeAbstract = false) : IQuickScientificSources
    {
        public string[] EnabledProviders => ["PubMed", "EuropePmc"];
        public string PrepareQuery(string provider, string query) => query;
        public Task<QuickProviderPage> SearchAsync(string provider, Guid runId, Guid attemptId, string query, CancellationToken token) =>
            Task.FromResult(new QuickProviderPage(1, [new("123", null, "10.1234/creatine", "Creatine and memory.",
                includeAbstract ? "This abstract describes creatine and memory outcomes in adults. Context is limited." : null, null, null, 2024,
                [], [], new(attemptId, provider, "123"))], 0));
    }

    private sealed class ApiAnswerLlm : IStructuredLlmClient
    {
        public int Calls { get; private set; }
        public Task<StructuredGenerationResult<T>> GenerateStructuredAsync<T>(StructuredLlmRequest request, CancellationToken token)
        {
            Calls++;
            using var data = JsonDocument.Parse(request.UserPrompt);
            var source = data.RootElement.GetProperty("publications")[0];
            var id = source.GetProperty("sourceId").GetGuid();
            const string text = "В аннотации описано влияние креатина на память.";
            var draft = new QuickAnswerDraft(text, [new(text, [new(id, source.GetProperty("abstractText").GetString()!)])], [],
                ["Доступный материал не позволяет делать выводы для каждого человека."], "VeryLimited", [id]);
            return Task.FromResult(new StructuredGenerationResult<T>((T)(object)draft,
                new("Fake", "not-public-model", null, DateTimeOffset.UtcNow)));
        }
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
