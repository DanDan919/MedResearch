using System.Net.Http.Json;
using MedResearch.Api.Research;
using MedResearch.Application.Research.Admission;
using MedResearch.Infrastructure.Persistence;
using MedResearch.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedResearch.IntegrationTests;

internal static class ResearchCreateTestClient
{
    public static async Task<HttpResponseMessage> PostResearchAsync(this HttpClient client, CreateResearchRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/research") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return await client.SendAsync(request);
    }

    // Existing shared-fixture scientific/read tests are not admission-capacity scenarios.
    public static EfResearchStore SharedFixtureStore(MedResearchDbContext context) => new(context,
        new ResearchAdmissionOptions { OwnerOutstandingLimit = 10000, GlobalOutstandingLimit = 10000,
            OwnerDailyLimit = 10000, GlobalDailyLimit = 10000 },
        new PostgreSqlResearchAdmissionClock(), NullLogger<EfResearchStore>.Instance);
}
