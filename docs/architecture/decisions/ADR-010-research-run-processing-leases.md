# ADR-010: ResearchRun Processing Leases And Recovery

Date: 2026-09-01

## Status

Accepted

## Context

The background worker claims a `ResearchRun`, commits that claim, and then performs stage work outside the database transaction. That is the right transaction boundary because planning, PubMed retrieval, extraction, evaluation, and synthesis may perform external I/O and must not hold PostgreSQL locks for the duration of those calls.

The drawback is crash behavior. A process can claim a run, move it to an in-progress state, and then disappear before persisting a terminal state. Without persisted ownership metadata, another worker cannot distinguish healthy work from abandoned work.

The pipeline already has useful idempotency boundaries:

- one `ResearchPlan` per `ResearchRun`;
- one `LiteratureSearch` row per search execution, with unique run/study discovery links;
- one `EvidenceExtraction` per run/study/prompt version;
- one `EvidenceEvaluation` per run/study/prompt version;
- one `ResearchReport` per run/prompt version.

Those boundaries make retrying the current stage practical as long as concurrent stale writers are fenced out.

## Decision

Persist processing lease metadata on `ResearchRun`:

- `ProcessingLeaseOwner`
- `ProcessingLeaseAcquiredAt`
- `ProcessingLeaseExpiresAt`
- `LastHeartbeatAt`
- `ProcessingLeaseVersion`

A worker gets an operational instance id at startup. Claim/reclaim uses one PostgreSQL `UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED ...) RETURNING ...` statement. A queued run is moved to `Planning`; an abandoned in-progress run is reclaimed at its current stage. Reclaimable statuses are `Planning`, `Searching`, `Extracting`, `Evaluating`, and `Synthesizing`. Terminal states are not reclaimable.

Each claim increments `ProcessingLeaseVersion`. Progress, failure, lease renewal, and lease release operations require the current run id, lease owner, and lease version to still match. This is a fencing token: if worker A stalls, worker B reclaims after expiry, and worker A later wakes up, worker A cannot overwrite B's valid ownership or later progress.

The worker renews the lease before each stage and runs a lightweight heartbeat loop during stage execution. `ResearchProcessing:LeaseDurationSeconds` and `ResearchProcessing:HeartbeatIntervalSeconds` are configurable and validated so the heartbeat interval is positive and shorter than the lease duration.

Terminal states clear active lease metadata. On stage failure, the owning worker marks the run `Failed` with the existing safe failure reason and clears the lease. On host shutdown cancellation, the worker attempts to release its lease without marking scientific failure; if release is not possible, the lease expires naturally.

## Consequences

Stale in-progress runs can be recovered by another worker without resetting the entire run to `Queued`. Recovery resumes from the persisted current status and relies on stage-level idempotency to avoid duplicate accepted artifacts.

The worker logs claim, reclaim, lease renewal, stage start/completion/failure, lease loss, and completion events with `ResearchRunId`, `WorkerId`, `LeaseVersion`, stage, duration, and expiry where useful. Scientific payloads are not logged.

## Tradeoffs

Lease expiry does not prove the old process is dead; it proves only that ownership is stale enough to transfer. The `ProcessingLeaseVersion` fencing token is therefore required for lease-sensitive writes.

The heartbeat is part of the processor execution path rather than a separate scheduler subsystem. This keeps the current monolith simple while covering long-running stage calls.

Transactions are still short and are not held across external OpenAI or PubMed I/O. That means a retried stage may re-enter provider or store code, so idempotency constraints remain essential.

## Verification

PostgreSQL integration tests cover exclusive claim, concurrent claim behavior, reclaim after expiry, heartbeat renewal, stale-owner write rejection, terminal-state non-reclaimability, different-run concurrent claims, and single-run concurrent reclaim. A full fake-provider vertical integration test covers the complete pipeline without live OpenAI, PubMed, or arbitrary network calls.
