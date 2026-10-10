# ADR-033: Atomic Owner-Scoped Research Admission

- Status: Accepted
- Date: 2026-10-10

## Context

Scientific stage bounds do not bound authenticated create volume. Parallel POSTs
could fill an unlimited queued backlog and lost responses could duplicate work.
SAAS-003 is a small pilot admission policy, not billing or cost metering.

## Decision

Reuse EfResearchStore's create transaction. Choose one transaction-scoped global
PostgreSQL advisory lock `(1297237323, 1)`, not guard rows or process-local locks.
READ COMMITTED ensures queries after waiting observe committed admissions.
The namespace is reserved for all research-create writers in this database.
Serializing a small pilot's short creation path is simpler than several owner/day
guards, insert races and deterministic multi-lock ordering. No external I/O is
allowed inside this transaction.

Require a non-empty UUID Idempotency-Key, scoped to authenticated `sub`. Fingerprint
the accepted trimmed question using SHA-256 over `research-create-v1\n` plus the
text; internal whitespace/case/Unicode are not silently rewritten. Store no extra
question text. Resolve committed same-body replays before stop/quota and return
the original 201/Queued response/Location; different body conflicts. GET remains
the authoritative current lifecycle status. Never generate a new server key on
each retry. Database PK(owner,key), unique run FK, and one transaction protect the
mapping and insertion. A rejected/rolled-back key is not permanently consumed.

Derive outstanding counts from all nonterminal ResearchRuns; lease expiry is not
completion. Read PostgreSQL clock_timestamp after the lock for UTC calendar-day
reservations. Count legacy runs without a reservation using their CreatedAt,
without inventing/backfilling historical client keys. New accepted daily usage is
independent of API clocks and later scientific outcome. No terminal refund.

Use the same bounded typed policy on every replica: defaults 1/2 outstanding and
2/10 daily (owner/global); ranges 1..10000, owner <= global. Invalid configuration
fails startup. StopNewAdmissions blocks new work only; options bind at startup,
so operators must restart all replicas consistently. Replay/read/worker processing
remain available. No dynamic admin endpoint or kill switch is added.

Keep BFF private-session/JWT/CSRF/allowlist/body-cap/no-store guarantees. Forward
only validated create keys and bounded allowlisted public errors. The form keeps
one key for a mounted same-question submission, including ambiguous retries;
reload/new-form durability is not claimed.

## Consequences and Verification

The ledger retains personal owner identifiers and hashes; hashes are not
encryption against guessing known questions. Admission history has no automatic
purge. Restrictive Run deletion protects accounting; future retention/deletion
needs its own explicit accounting/replay design.
Direct SQL writers/manual deletions, mismatched replica configuration, clock
changes on the PostgreSQL server, and loss/restore of the database remain outside
the single-database atomicity guarantee. Lock contention is a pilot trade-off;
there is no latency/throughput certification or queue-wait cap added here.

PostgreSQL/Testcontainers tests cover separate API hosts/connections and separate
OS API processes, concurrent distinct/repeated keys, daily rollover, stop,
constraints, rollback/cancellation and worker reclaim/fencing. Local Docker skips
are explicit; CI requires zero skips. Production JWT ownership and the trusted
HTTPS full-stack browser gates remain independent regressions.

Accepted-run count is not token accounting, a per-run dollar ceiling, global
real-time spend control, lifetime Study bound, or exactly-once paid calls.
Those remain SAAS-002 work. No scientific calculator/stage algorithm changes.
