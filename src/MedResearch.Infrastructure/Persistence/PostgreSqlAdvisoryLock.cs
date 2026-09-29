using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MedResearch.Infrastructure.Persistence;

internal static class PostgreSqlAdvisoryLock
{
    public static Task AcquireTransactionLockAsync(
        DatabaseFacade database,
        string lockKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);

        return database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}
