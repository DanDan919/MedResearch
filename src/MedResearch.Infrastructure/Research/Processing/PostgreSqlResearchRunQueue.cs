using System.Data;
using System.Data.Common;
using MedResearch.Application.Research.Processing;
using MedResearch.Domain;
using MedResearch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedResearch.Infrastructure.Research.Processing;

public sealed class PostgreSqlResearchRunQueue : IResearchRunQueue
{
    private readonly MedResearchDbContext _dbContext;

    public PostgreSqlResearchRunQueue(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ResearchRun?> TryClaimNextQueuedRunAsync(
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var connection = _dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            UPDATE research_runs
            SET status = @planning_status,
                started_at = @claimed_at
            WHERE id = (
                SELECT id
                FROM research_runs
                WHERE status = @queued_status
                ORDER BY created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING id, research_question_id, status, created_at, started_at, completed_at, failure_reason;
            """;

        AddParameter(command, "planning_status", ResearchRunStatus.Planning.ToString());
        AddParameter(command, "claimed_at", claimedAt);
        AddParameter(command, "queued_status", ResearchRunStatus.Queued.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var run = new ResearchRun(
            reader.GetGuid(0),
            reader.GetGuid(1),
            Enum.Parse<ResearchRunStatus>(reader.GetString(2)),
            ReadDateTimeOffset(reader, 3),
            reader.IsDBNull(4) ? null : ReadDateTimeOffset(reader, 4),
            reader.IsDBNull(5) ? null : ReadDateTimeOffset(reader, 5),
            reader.IsDBNull(6) ? null : reader.GetString(6));

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);

        return run;
    }

    public async Task SaveProgressAsync(ResearchRun run, CancellationToken cancellationToken)
    {
        _dbContext.ResearchRuns.Update(run);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> MarkFailedAsync(
        Guid researchRunId,
        string safeFailureReason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        var run = await _dbContext.ResearchRuns
            .SingleOrDefaultAsync(candidate => candidate.Id == researchRunId, cancellationToken);

        if (run is null)
        {
            return false;
        }

        if (run.Status is ResearchRunStatus.Completed or ResearchRunStatus.Failed or ResearchRunStatus.Cancelled)
        {
            return false;
        }

        run.Fail(safeFailureReason, failedAt);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);

        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException($"Unexpected timestamp value type '{value.GetType().Name}'.")
        };
    }
}
