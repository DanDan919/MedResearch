using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class MigrationRuntimeTests
{
    private readonly PostgreSqlFixture _fixture;

    public MigrationRuntimeTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task EfMigrations_ApplyToFreshPostgreSqlDatabase()
    {
        SkipIfPostgreSqlUnavailable();

        await using var context = _fixture.CreateDbContext();

        var configuredMigrations = context.Database.GetMigrations().ToArray();
        var appliedMigrations = context.Database.GetAppliedMigrations().ToArray();
        var pendingMigrations = context.Database.GetPendingMigrations().ToArray();

        Assert.NotEmpty(configuredMigrations);
        Assert.Contains("20260901063528_AddResearchRunProcessingLeases", configuredMigrations);
        Assert.Equal(configuredMigrations, appliedMigrations);
        Assert.Empty(pendingMigrations);
    }

    private void SkipIfPostgreSqlUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            Skip.IfNot(_fixture.IsAvailable, $"Docker-backed PostgreSQL integration tests skipped: {_fixture.UnavailableReason}");
        }
    }
}
