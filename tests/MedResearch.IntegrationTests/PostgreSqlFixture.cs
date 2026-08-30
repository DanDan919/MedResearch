using DotNet.Testcontainers.Builders;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace MedResearch.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public DbContextOptions<MedResearchDbContext> DbContextOptions { get; private set; } = null!;

    public string? UnavailableReason { get; private set; }

    public bool IsAvailable => UnavailableReason is null;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("medresearch_tests")
                .WithUsername("medresearch")
                .WithPassword("medresearch_dev_password")
                .Build();

            await _postgres.StartAsync();

            DbContextOptions = new DbContextOptionsBuilder<MedResearchDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;

            await using var context = CreateDbContext();
            await context.Database.MigrateAsync();
        }
        catch (DockerUnavailableException exception)
        {
            UnavailableReason = exception.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    public MedResearchDbContext CreateDbContext()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("PostgreSQL test database is unavailable.");
        }

        return new MedResearchDbContext(DbContextOptions);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration tests";
}
