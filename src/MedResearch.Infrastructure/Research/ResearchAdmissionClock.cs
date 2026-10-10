using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedResearch.Infrastructure.Research;

public interface IResearchAdmissionClock
{
    Task<DateTimeOffset> ReadUtcNowAsync(MedResearchDbContext context, CancellationToken cancellationToken);
}

public sealed class PostgreSqlResearchAdmissionClock : IResearchAdmissionClock
{
    public async Task<DateTimeOffset> ReadUtcNowAsync(MedResearchDbContext context, CancellationToken cancellationToken)
    {
        var value = await context.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);
        return new DateTimeOffset(value);
    }
}
