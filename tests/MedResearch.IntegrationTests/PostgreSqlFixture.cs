using DotNet.Testcontainers.Builders;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace MedResearch.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public DbContextOptions<MedResearchDbContext> DbContextOptions { get; private set; } = null!;

    public string? ConnectionString { get; private set; }

    public string? UnavailableReason { get; private set; }

    public bool IsAvailable => UnavailableReason is null;

    public static bool RequireDockerTests => IsTruthy(Environment.GetEnvironmentVariable("MEDRESEARCH_REQUIRE_DOCKER_TESTS"));

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
            ConnectionString = _postgres.GetConnectionString();

            DbContextOptions = new DbContextOptionsBuilder<MedResearchDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

            await using var context = CreateDbContext();
            await context.Database.MigrateAsync();
        }
        catch (DockerUnavailableException exception) when (!RequireDockerTests)
        {
            UnavailableReason = exception.Message;
        }
        catch (DockerUnavailableException exception) when (RequireDockerTests)
        {
            throw new InvalidOperationException(
                "Docker-backed PostgreSQL integration tests are required, but Docker/Testcontainers is unavailable.",
                exception);
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

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration tests";
}