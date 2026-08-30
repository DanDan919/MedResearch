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

    public async Task<ClaimedResearchRun?> TryClaimNextQueuedRunAsync(
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
            UPDATE research_runs AS run
            SET status = @planning_status,
                started_at = @claimed_at
            FROM research_questions AS question
            WHERE run.id = (
                SELECT candidate.id
                FROM research_runs AS candidate
                WHERE candidate.status = @queued_status
                ORDER BY candidate.created_at, candidate.id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            AND question.id = run.research_question_id
            RETURNING run.id,
                run.research_question_id,
                run.status,
                run.created_at,
                run.started_at,
                run.completed_at,
                run.failure_reason,
                question.text;
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
        var question = reader.GetString(7);

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);

        return new ClaimedResearchRun(run, question);
    }

    public async Task SaveProgressAsync(ClaimedResearchRun claimedRun, CancellationToken cancellationToken)
    {
        _dbContext.ResearchRuns.Update(claimedRun.Run);
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
