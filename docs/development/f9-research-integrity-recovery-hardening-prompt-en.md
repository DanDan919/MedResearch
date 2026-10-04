# F9 — Research Integrity, Recovery Idempotency, Failure Provenance, and CI Browser Confidence

Repository: `E:\\MedResearch`
Remote: `https://github.com/DanDan919/MedResearch.git`
Branch: `main`

## Mission

Continue the existing MedResearch project from the actual current repository state. This is a reliability and architecture-hardening milestone, not a new scientific feature milestone.

Before editing, inspect the actual repository. Run and report:

```text
git status
git branch
git remote -v
git log -n 10 --oneline
git diff --check
```

Do not reset, clean, restore, rewrite history, or discard unrelated local changes. Do not assume any historical HEAD or CI run is still current.

Read `AGENTS.md`, `README.md`, `ARCHITECTURE.md`, current-state/problem/discovery/technical-debt documents, all relevant ADRs, the worker/recovery code, all stage stores, literature search orchestration, report/quantitative artifact code, frontend API schemas, Playwright tests, and `.github/workflows/ci.yml` before making production changes.

## Findings that must be verified first

The pre-audit found these concrete risks. Reproduce each against the current code before fixing it:

1. `EfResearchPlanStore.SaveResearchPlanAsync` always inserts a plan, while `ResearchPlanConfiguration` has a unique `ResearchRunId` index. A crash after plan persistence and before the stage transition can make recovery fail with a duplicate-plan error.
2. Re-entering `Searching` generates a new search execution and repeats provider calls/provenance writes. Study deduplication does not make external search execution idempotent.
3. A partial provider failure is logged and omitted from persisted search provenance when another provider succeeds. Zero results, transient failure, permanent failure, timeout, and cancellation are not currently represented with equal clarity.
4. `SynthesisContextBuilder` currently treats an empty evidence set as `UsesAbstractLevelEvidenceOnly=true`; absence of evidence must not be described as abstract evidence.
5. The frontend CI workflow does not currently run the Playwright suite, although local F8 verification reported seven browser tests.

If the current code has changed, trust code and tests over this prompt and update the implementation plan accordingly. Do not manufacture defects.

## Scope

Implement only the reliability corrections required by the findings above and their regression tests.

Do not add another scientific source, AI capability, embeddings, RAG, vector storage, authentication system, microservice, queue platform, or unrelated UI feature. Do not require `OPENAI_API_KEY`, NCBI credentials, Europe PMC credentials, or live internet in normal tests.

## 1. Planning retry idempotency

Make planning persistence safe across a crash window:

- one run must have at most one canonical persisted plan;
- retrying the same run with the same effective question/plan input and prompt/model provenance must reuse the existing plan;
- a conflicting plan for the same run must be rejected deterministically and must not overwrite the existing plan;
- all writes must retain the existing ResearchRun lease/write-fencing guarantees;
- no database transaction may remain open across an LLM/network call.

Choose the smallest design consistent with the existing domain and schema. A forward migration is allowed only if genuinely required; never edit an old migration.

Add tests for:

- first save;
- same-input retry/reuse;
- conflicting retry;
- crash-equivalent retry after plan save;
- stale lease owner cannot write a plan after ownership transfer;
- real PostgreSQL unique/concurrency behavior when practical.

Do not silently accept a different planner output merely because the run already has a plan.

## 2. Searching retry idempotency and provenance

Inspect the existing coordinator and `LiteratureSearch` model. Define a deterministic idempotency key for a planned query executed against a source, preferably based on the current run, plan, normalized query, source identity, and any execution ordinal needed to distinguish deliberate repeated queries.

Implement conservative reuse semantics:

- if the same successful source execution already exists, do not call the external provider again;
- do not create duplicate successful `LiteratureSearch` rows or duplicate discovery rows;
- preserve one global canonical Study and per-search provenance;
- preserve distinct downstream Study work per ResearchRun;
- concurrent workers must converge using PostgreSQL uniqueness/transaction semantics, not a check-then-insert race;
- stale workers must not write after lease ownership changes.

Do not erase legitimate separate searches. The identity must distinguish separate planned queries and sources while making a retry of the same execution recognizable.

Explicitly decide how to represent source attempts that fail. Prefer a small, explicit status/error model if it fits the current architecture. If a schema change is necessary, add a forward migration. At minimum, make the following distinctions testable and documented:

- successful search with results;
- successful search with zero results;
- transient provider failure;
- permanent/provider configuration failure;
- cancellation.

A partial multi-source failure must not be silently presented as if every configured source succeeded. It may still allow the stage to continue when policy says one successful source is sufficient, but the failed attempt and its effect must be observable and deterministic.

Add tests for:

- repeated search-stage execution;
- duplicate query IDs returned by a provider;
- same publication discovered by PubMed and Europe PMC;
- two distinct queries discovering one Study;
- one source failing while another succeeds;
- all sources failing;
- zero results versus provider failure;
- cancellation;
- concurrent retry of one execution;
- no duplicate external fake-provider call for a reused execution.

## 3. Empty-evidence semantics

Correct `UsesAbstractLevelEvidenceOnly` and any related coverage fields so that:

- empty evidence means no validated evidence, not abstract-only evidence;
- non-empty abstract evidence may be reported as abstract-only;
- richer source material is not downgraded;
- insufficient-evidence reports and API projections remain semantically consistent.

Add a focused regression test and update any fixture helper that currently encodes the wrong value. Do not change valid non-empty evidence behavior.

## 4. Write-fencing audit

Audit every production stage writer, including plans, source material, search results, extraction, evaluation, synthesis, quantitative artifacts, and reports. Verify through code and tests that:

- the DI production path supplies the real `IResearchRunWriteFence`;
- the fence check is inside the same short transaction as the write;
- owner and lease version are checked where required;
- stale workers cannot overwrite newer state or mark a newer run failed;
- terminal states clear/ignore lease metadata consistently.

Add a lightweight architecture/DI test or targeted integration tests if the existing suite would not catch a removed fence.

## 5. Frontend CI confidence

Update `.github/workflows/ci.yml` so normal CI runs the deterministic Playwright suite. Use official actions and the repository’s existing package manager. Install only the required browser/dependencies, run the existing `pnpm test:e2e`, and preserve the existing lint, typecheck, unit test, build, API generation, backend, migration, and Docker Compose checks.

The browser tests must not call OpenAI, PubMed, Europe PMC, or a production database. They may use the existing mocked API boundary. CI must fail if the browser suite fails; it must not be silently omitted.

Do not add a live provider smoke test to normal CI.

## 6. Cross-run and cross-layer regression audit

Retain and strengthen tests proving:

- ResearchQuestion may own multiple independent ResearchRuns;
- plans, searches, evidence, evaluations, reports, claims, quantitative artifacts, and citations cannot cross-contaminate runs;
- a report claim can cite only current-run evidence;
- source/provider provenance survives deduplication;
- Study identity is not merged by title, author similarity, or year;
- missing metadata stays missing and conflicting identifiers are not silently overwritten;
- frontend query keys and API schemas remain scoped to the requested run.

Prefer negative tests that intentionally use the wrong run ID, wrong evidence ID, wrong lease version, wrong source lineage, and an unknown enum value.

## 7. Verification requirements

Run locally what the environment allows:

```text
dotnet restore MedResearch.slnx
dotnet build MedResearch.slnx --no-restore
dotnet test MedResearch.slnx --no-build
dotnet ef migrations has-pending-model-changes
docker compose config
git diff --check
pnpm test --run
pnpm lint
pnpm typecheck
pnpm test:e2e
```

If Docker is unavailable locally, report PostgreSQL/Testcontainers skips honestly and do not call them passes. Do not spend the milestone repairing Docker Desktop.

Commit and push only after the user-authorized repository workflow permits it. Use a focused commit such as:

`fix: harden research recovery and CI verification`

In CI, require:

- one Linux runner;
- real PostgreSQL/Testcontainers tests with `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`;
- zero required Docker-test skips;
- deterministic frontend tests and Playwright execution;
- no live OpenAI, PubMed, or Europe PMC calls;
- no repository secrets.

## Final report

Report, with exact evidence rather than assumptions:

1. actual starting and final HEAD, branch, remote, and working-tree status;
2. each confirmed defect, severity, root cause, fix, and regression test;
3. planning retry policy;
4. searching idempotency key and partial-failure semantics;
5. zero-result versus failure behavior;
6. write-fencing coverage;
7. empty-evidence semantic behavior;
8. frontend Playwright CI step and exact count;
9. Domain/Application/Infrastructure/Integration/PostgreSQL/frontend test totals, including skips and reasons;
10. migration status and any new migration;
11. CI run URL/ID, Docker availability, PostgreSQL execution, browser execution, failures and required skips;
12. files and documentation changed;
13. remaining risks, including live-provider availability, authentication, and any intentionally deferred schema/provenance limitations;
14. commit and push status.

Do not claim production-grade recovery, PostgreSQL runtime confidence, or CI browser confidence unless the corresponding tests actually executed successfully on the reported commit.
