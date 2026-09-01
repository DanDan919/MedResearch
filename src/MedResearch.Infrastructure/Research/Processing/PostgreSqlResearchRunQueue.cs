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
    private static readonly string[] RecoverableStatuses =
    [
        ResearchRunStatus.Planning.ToString(),
        ResearchRunStatus.Searching.ToString(),
        ResearchRunStatus.Extracting.ToString(),
        ResearchRunStatus.Evaluating.ToString(),
        ResearchRunStatus.Synthesizing.ToString()
    ];

    private readonly MedResearchDbContext? _dbContext;
    private readonly IDbContextFactory<MedResearchDbContext>? _dbContextFactory;

    public PostgreSqlResearchRunQueue(MedResearchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public PostgreSqlResearchRunQueue(IDbContextFactory<MedResearchDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public Task<ClaimedResearchRun?> TryClaimNextQueuedRunAsync(
        DateTimeOffset claimedAt,
        string workerInstanceId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerInstanceId);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }

        return UseContextAsync(context => TryClaimNextQueuedRunAsync(context, claimedAt, workerInstanceId, leaseDuration, cancellationToken));
    }

    public Task<bool> RenewLeaseAsync(
        ClaimedResearchRun claimedRun,
        DateTimeOffset heartbeatAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedRun);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }

        return UseContextAsync(context => ExecuteLeaseUpdateAsync(
            context,
            """
            UPDATE research_runs
            SET last_heartbeat_at = @heartbeat_at,
                processing_lease_expires_at = @lease_expires_at
            WHERE id = @id
            AND processing_lease_owner = @worker_id
            AND processing_lease_version = @lease_version
            AND status = ANY(@active_statuses)
            RETURNING 1;
            """,
            command =>
            {
                AddParameter(command, "heartbeat_at", heartbeatAt);
                AddParameter(command, "lease_expires_at", heartbeatAt.Add(leaseDuration));
                AddLeaseParameters(command, claimedRun);
                AddParameter(command, "active_statuses", RecoverableStatuses);
            },
            cancellationToken));
    }

    public Task<bool> SaveProgressAsync(
        ClaimedResearchRun claimedRun,
        DateTimeOffset savedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedRun);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }

        return UseContextAsync(context => SaveProgressAsync(context, claimedRun, savedAt, leaseDuration, cancellationToken));
    }

    public Task<bool> MarkFailedAsync(
        ClaimedResearchRun claimedRun,
        string safeFailureReason,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedRun);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeFailureReason);

        return UseContextAsync(context => ExecuteLeaseUpdateAsync(
            context,
            """
            UPDATE research_runs
            SET status = @failed_status,
                completed_at = @failed_at,
                failure_reason = @failure_reason,
                processing_lease_owner = NULL,
                processing_lease_acquired_at = NULL,
                processing_lease_expires_at = NULL,
                last_heartbeat_at = NULL
            WHERE id = @id
            AND processing_lease_owner = @worker_id
            AND processing_lease_version = @lease_version
            AND status = ANY(@active_statuses)
            RETURNING 1;
            """,
            command =>
            {
                AddParameter(command, "failed_status", ResearchRunStatus.Failed.ToString());
                AddParameter(command, "failed_at", failedAt);
                AddParameter(command, "failure_reason", safeFailureReason);
                AddLeaseParameters(command, claimedRun);
                AddParameter(command, "active_statuses", RecoverableStatuses);
            },
            cancellationToken));
    }

    public Task<bool> ReleaseLeaseAsync(ClaimedResearchRun claimedRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedRun);

        return UseContextAsync(context => ExecuteLeaseUpdateAsync(
            context,
            """
            UPDATE research_runs
            SET processing_lease_owner = NULL,
                processing_lease_acquired_at = NULL,
                processing_lease_expires_at = NULL,
                last_heartbeat_at = NULL
            WHERE id = @id
            AND processing_lease_owner = @worker_id
            AND processing_lease_version = @lease_version
            AND status = ANY(@active_statuses)
            RETURNING 1;
            """,
            command =>
            {
                AddLeaseParameters(command, claimedRun);
                AddParameter(command, "active_statuses", RecoverableStatuses);
            },
            cancellationToken));
    }

    private async Task<ClaimedResearchRun?> TryClaimNextQueuedRunAsync(
        MedResearchDbContext dbContext,
        DateTimeOffset claimedAt,
        string workerInstanceId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            WITH candidate AS (
                SELECT id, status
                FROM research_runs
                WHERE status = @queued_status
                   OR (
                        status = ANY(@recoverable_statuses)
                        AND (
                            processing_lease_owner IS NULL
                            OR processing_lease_expires_at IS NULL
                            OR processing_lease_expires_at <= @claimed_at
                        )
                   )
                ORDER BY CASE WHEN status = @queued_status THEN 0 ELSE 1 END, created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE research_runs AS run
            SET status = CASE WHEN run.status = @queued_status THEN @planning_status ELSE run.status END,
                started_at = COALESCE(run.started_at, @claimed_at),
                processing_lease_owner = @worker_id,
                processing_lease_acquired_at = @claimed_at,
                processing_lease_expires_at = @lease_expires_at,
                last_heartbeat_at = @claimed_at,
                processing_lease_version = run.processing_lease_version + 1
            FROM candidate, research_questions AS question
            WHERE run.id = candidate.id
            AND question.id = run.research_question_id
            RETURNING run.id,
                run.research_question_id,
                run.status,
                run.created_at,
                run.started_at,
                run.completed_at,
                run.failure_reason,
                run.processing_lease_owner,
                run.processing_lease_acquired_at,
                run.processing_lease_expires_at,
                run.last_heartbeat_at,
                run.processing_lease_version,
                question.text,
                candidate.status <> @queued_status;
            """;

        AddParameter(command, "queued_status", ResearchRunStatus.Queued.ToString());
        AddParameter(command, "planning_status", ResearchRunStatus.Planning.ToString());
        AddParameter(command, "recoverable_statuses", RecoverableStatuses);
        AddParameter(command, "claimed_at", claimedAt);
        AddParameter(command, "lease_expires_at", claimedAt.Add(leaseDuration));
        AddParameter(command, "worker_id", workerInstanceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var claimedRun = ReadClaimedResearchRun(reader, workerInstanceId);

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);

        return claimedRun;
    }

    private async Task<bool> SaveProgressAsync(
        MedResearchDbContext dbContext,
        ClaimedResearchRun claimedRun,
        DateTimeOffset savedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var run = claimedRun.Run;
        var isTerminal = run.Status is ResearchRunStatus.Completed or ResearchRunStatus.Failed or ResearchRunStatus.Cancelled;

        return await ExecuteLeaseUpdateAsync(
            dbContext,
            """
            UPDATE research_runs
            SET status = @status,
                started_at = @started_at,
                completed_at = @completed_at,
                failure_reason = @failure_reason,
                last_heartbeat_at = CASE WHEN @is_terminal THEN NULL ELSE @saved_at END,
                processing_lease_expires_at = CASE WHEN @is_terminal THEN NULL ELSE @lease_expires_at END,
                processing_lease_owner = CASE WHEN @is_terminal THEN NULL ELSE processing_lease_owner END,
                processing_lease_acquired_at = CASE WHEN @is_terminal THEN NULL ELSE processing_lease_acquired_at END
            WHERE id = @id
            AND processing_lease_owner = @worker_id
            AND processing_lease_version = @lease_version
            RETURNING 1;
            """,
            command =>
            {
                AddParameter(command, "status", run.Status.ToString());
                AddParameter(command, "started_at", run.StartedAt);
                AddParameter(command, "completed_at", run.CompletedAt);
                AddParameter(command, "failure_reason", run.FailureReason);
                AddParameter(command, "is_terminal", isTerminal);
                AddParameter(command, "saved_at", savedAt);
                AddParameter(command, "lease_expires_at", savedAt.Add(leaseDuration));
                AddLeaseParameters(command, claimedRun);
            },
            cancellationToken);
    }

    private async Task<bool> ExecuteLeaseUpdateAsync(
        MedResearchDbContext dbContext,
        string commandText,
        Action<DbCommand> configure,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        configure(command);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && result is not DBNull;
    }

    private async Task<T> UseContextAsync<T>(Func<MedResearchDbContext, Task<T>> action)
    {
        if (_dbContextFactory is not null)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            return await action(dbContext);
        }

        if (_dbContext is null)
        {
            throw new InvalidOperationException("No database context is available for the research run queue.");
        }

        return await action(_dbContext);
    }

    private static ClaimedResearchRun ReadClaimedResearchRun(DbDataReader reader, string workerInstanceId)
    {
        var run = new ResearchRun(
            reader.GetGuid(0),
            reader.GetGuid(1),
            Enum.Parse<ResearchRunStatus>(reader.GetString(2)),
            ReadDateTimeOffset(reader, 3),
            reader.IsDBNull(4) ? null : ReadDateTimeOffset(reader, 4),
            reader.IsDBNull(5) ? null : ReadDateTimeOffset(reader, 5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : ReadDateTimeOffset(reader, 8),
            reader.IsDBNull(9) ? null : ReadDateTimeOffset(reader, 9),
            reader.IsDBNull(10) ? null : ReadDateTimeOffset(reader, 10),
            reader.GetInt64(11));
        var question = reader.GetString(12);
        var wasReclaimed = reader.GetBoolean(13);

        return new ClaimedResearchRun(
            run,
            question,
            workerInstanceId,
            run.ProcessingLeaseVersion,
            run.ProcessingLeaseExpiresAt ?? throw new InvalidOperationException("Claimed research run did not include a lease expiry."),
            wasReclaimed);
    }

    private static void AddLeaseParameters(DbCommand command, ClaimedResearchRun claimedRun)
    {
        AddParameter(command, "id", claimedRun.Run.Id);
        AddParameter(command, "worker_id", claimedRun.WorkerInstanceId);
        AddParameter(command, "lease_version", claimedRun.LeaseVersion);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
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