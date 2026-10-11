# ADR-035: Owner-authorized synthesis-only recovery

- Status: Accepted
- Date: 2026-10-11

## Context

SCI-HF01 corrected bounded report generation but a Failed run could not reuse its
accepted scientific work. Artifact counts and a generic failure message cannot
prove the failed stage. Lease reclaim of active work is not authorization to
reopen a terminal run or replay uncertain paid requests.

## Decision

Record nullable FailedStage and bounded FailureCategory with fenced worker failure.
Legacy null markers remain null. Admit only an authenticated owning caller's
Failed/Synthesizing run with no report and a valid same-run source/evidence/
evaluation/quantitative graph. Revalidate and fingerprint the persisted inputs.
Use one synthesis_retry_attempts row per run, enforced by a unique restrictive FK.
This deliberately fixes the initial policy at one owner retry, not a generic
workflow or configurable unlimited retries. Store the scoped UUID key's hash.

Admission locks the owning run FOR UPDATE, resolves an existing key first, then
checks eligibility and commits the retry without changing Failed. Claim uses the
existing SKIP LOCKED transaction: a Failed run with a queued retry becomes
Synthesizing, gets an incremented lease version, and its attempt becomes Running.
No Planning/Searching/Extracting/Evaluating transition is invoked. Reclaim retains
Synthesizing and updates the retry's fencing version. Failure and terminal progress
settle the attempt in the same SQL statement as the run. Completion requires an
authoritative report for retry claims.

Before every synthesizer generation (including semantic repair), a short fenced
transaction reserves a durable call and marks it pending. The admission-time
budget is 1 + MaxSemanticRepairAttempts (1..3, default 2). No reservation is
refunded. Receipt is acknowledged durably before validation; invalid replacements
remain untrusted. Pending on reclaim or timeout is OutcomeUnknown and stops
automatic generation, even with numerical budget left. Reclaim before reservation
can proceed. Received-but-not-persisted results may use remaining budget only;
accepted reports are reused after a crash without another generation. This is
bounded at-least-once dispatch, not exactly-once billing or a dollar cap.

The normal stage executor, corpus/structured validators, deterministic calculators
and report store remain authoritative. Retry compares existing quantitative
artifacts; it cannot manufacture missing snapshots or modify previous artifacts.
Inputs are checked before dispatch and again inside fenced report persistence.
Transactions never span LLM or provider I/O. Existing earlier scientific output
and owner-filtered partial reads remain available.

Expose authenticated GET/POST /api/research/{id}/retry-synthesis. POST requires an
empty JSON object and Idempotency-Key; owner comes only from authenticated sub.
Foreign/unknown runs are indistinguishable 404s. Same key replays the operation;
different keys cannot bypass the lifetime run budget. The BFF retains private
bearer, CSRF/same-origin, method/path allowlist, bounds and no-store policy.

## Consequences

Legacy failures remain unavailable, including failures with many accepted artifacts.
There is no operator override command in this change. An exceptional legacy
recovery needs independent original execution evidence, explicit operator approval,
a separate auditable attestation (not a forged failure marker), and the same ledger
and fences. See the Russian process document for the verification procedure.

Conservative fingerprints may reject recovery after shared metadata enrichment or
configuration/algorithm changes. GET eligibility is advisory; POST is authoritative.
Direct database writes/privileged deletion and forged worker scopes are outside the
application protocol. Retry admission is distinct from create's calendar-day quota
reservation: it creates no Question, Run or commercial reservation. This change
does not supply spend controls or general admission capacity for retries.

## Verification

Deterministic PostgreSQL/Testcontainers tests cover concurrent identical/different
keys, owner isolation, one leased execution, stale report/response/failure writes,
crash before/after reservation, bounded calls, input drift, invalid lineage, existing
report, timeout/unknown outcome, and ten-study synthesis-only completion with
zero previous-stage calls. Local Docker skips are not runtime certification;
strict CI must execute these tests without required skips.
