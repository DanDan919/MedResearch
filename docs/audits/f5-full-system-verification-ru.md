# M19/F5: полная adversarial-проверка MedResearch

Дата проверки: 2026-09-29. Проверка выполнена по фактическому коду, тестам,
миграциям, Docker/CI и frontend-контракту. `CODEX_CONTEXT.md` в репозитории не
обнаружен; это документный пробел, а не подтверждение отсутствующего правила.

## Исходное состояние

- Ветка: `main`.
- HEAD: `eda71adb202c4acec14670134f7ba2e2b12b4d1f`.
- Upstream: `origin/main`.
- Remote: `https://github.com/DanDan919/MedResearch.git`.
- До правок дерево было чистым, `git diff --check` успешен.
- Последний ранее подтвержденный F4 CI: `36547697996`, success.

## Фактическая карта исполнения

`POST /api/research` вызывает `CreateResearchUseCase`, который сохраняет
`ResearchQuestion` и queued `ResearchRun`. `ResearchRunProcessor` атомарно
получает run через `PostgreSqlResearchRunQueue` (`FOR UPDATE SKIP LOCKED`, lease
owner/version/expiry), затем `ScientificResearchStageExecutor` последовательно
вызывает:

1. `ResearchPlanner` -> `ResearchPlanValidator` -> `EfResearchPlanStore`;
2. `ScientificLiteratureSearchCoordinator` -> `IScientificLiteratureSource`
   (PubMed/Europe PMC) -> `EfScientificSearchResultStore`;
3. `SourceMaterialAcquirer` -> providers -> `EfSourceMaterialStore`;
4. `EvidenceExtractor`/`EvidenceExtractionDraftValidator` ->
   `EfEvidenceExtractionStore`;
5. `EvidenceEvaluator`/`EvidenceEvaluationDraftValidator` ->
   `EfEvidenceEvaluationStore`;
6. `EvidenceCorpusBuilder` -> `SynthesisContextBuilder` (включая
   deterministic quantitative synthesis) -> `ResearchSynthesizer` ->
   `ResearchReportDraftValidator` -> `EfResearchSynthesisStore`;
7. `GET /api/research/{id}/report` читает authoritative Study/Evidence graph.

API не содержит научных расчетов. Domain не ссылается на EF/ASP.NET/provider
transport. Application использует provider-neutral контракты. Infrastructure
содержит EF, PostgreSQL и внешние HTTP adapters.

## Matrix статусов

`VERIFIED` означает, что код и тесты подтверждают гарантию; `PARTIAL` означает,
что гарантия есть на application boundary, но не является DB invariant;
`TEST_ONLY` не является runtime enforcement; `DOC_ONLY` не считается доказанной
гарантией.

| Область | Статус | Основание/ограничение |
|---|---|---|
| Project dependency direction | VERIFIED | project references и namespace audit |
| ResearchRun lifecycle | VERIFIED | domain transitions + invalid-transition tests |
| Queue lease/fencing | VERIFIED | atomic SQL, PostgreSQL concurrency tests |
| ResearchQuestion -> multiple runs | VERIFIED | run-scoped stores/tests |
| Study global identity | VERIFIED | normalized PMID/PMCID/DOI + unique filtered indexes |
| Stable identifier conflict handling | VERIFIED | identity store conflict tests |
| No-ID automatic merge | VERIFIED | no fuzzy/title merge path |
| Search provenance per source/query | VERIFIED | separate LiteratureSearch + discovery rows |
| Discovery uniqueness | VERIFIED | `(literature_search_id, study_id)` unique index |
| Distinct downstream Study work | VERIFIED | grouped acquisition/extraction/synthesis queries |
| SourceMaterial hash integrity | VERIFIED | constructor recomputes hash/length; tests added in F5 |
| SourceMaterial current-version serialization | VERIFIED | PostgreSQL transaction advisory lock in both writers; integration test |
| Same-run Evidence extraction/evaluation | PARTIAL | application queries/validators; scalar FKs do not encode composite run+study |
| Evaluation EvidenceIds refer to same run | PARTIAL | application context controls IDs; `uuid[]` has no FK |
| Report claim -> Evidence same-run | PARTIAL | validator and read projection filter; join table FK cannot encode report-run/evidence-run equality |
| Evidence source grounding | PARTIAL | exact normalized substring/token grounding, not semantic truth verification |
| Quantitative arithmetic | VERIFIED | deterministic M17-M24 calculators; LLM receives result only |
| API liveness/readiness | VERIFIED | `/health/live` excludes dependencies; `/health/ready` checks PostgreSQL |
| Frontend scientific calculations | VERIFIED | UI renders backend fields and explicitly does not calculate |
| `CODEX_CONTEXT.md` | UNKNOWN/DOC GAP | file is absent |

## Adversarial findings and fixes

### Fixed: SourceMaterial integrity metadata (medium)

The public constructor previously accepted `ContentHash` and `CharacterCount`
without comparing them to normalized `Content`. A directly materialized or
malformed object could therefore claim a hash for different text. The
constructor now normalizes, recomputes SHA-256, canonicalizes the hash to lower
case, and rejects mismatched length/hash. `SourceMaterial.Create` already used
the correct computation.

### Fixed: concurrent source versioning (medium/high)

`EfSourceMaterialStore` and the search-result abstract writer both performed
read-current/next-version/update-current sequences. The database had no partial
unique current index, so concurrent changed-content writes could race. Both
writers now acquire the same PostgreSQL transaction advisory lock keyed by
study/type/provider/source-id. A real PostgreSQL test verifies two concurrent
writes produce versions 1 and 2 and exactly one current row.

### Documentation discrepancy corrected

The technical-debt note already claimed advisory-lock protection, but the source
material store did not implement it. F5 made the claim true and keeps the
remaining limitation explicit: the schema still lacks a partial unique current
index, so direct SQL outside the application lock protocol is not protected.

## Stage contract audit

| Stage | Input/output | Untrusted data and deterministic barrier | Retry/idempotency |
|---|---|---|---|
| Planning | question -> bounded plan | question equality, query/list/type/identifier bounds | run+plan uniqueness; provider failure fails run |
| Searching | plan queries -> search/discovery/Study | provider response normalization, stable IDs, conflict skip | per-source search row; zero results are successful; one source may fail while another succeeds |
| Extracting | current Study material -> grounded Evidence | source substring grounding, bounded findings, numeric token checks | run+study+source+prompt unique |
| Evaluating | same-run Evidence -> categorical evaluation | IDs are supplied by context; source limitations converted to unknown/insufficient | run+study+prompt unique |
| Synthesizing | validated corpus -> report/claims | claims may cite only supplied Evidence IDs; no model identifiers | run+prompt unique; report citation projection rechecks run/study lineage |

External cancellation is rethrown through stages and provider abstractions. A
successful empty search is persisted as a search result; HTTP/provider/parser
failure is logged and represented by the coordinator's partial/all-source
failure behavior.

## Trust and provenance limits

The LLM cannot supply authoritative PMID/DOI/StudyId for reports. Evidence text
must be contained in the supplied normalized source material, but this proves
textual grounding, not that a paper's scientific interpretation is true. Numeric
fields are token-grounded and eligibility-checked; they are not independently
reconstructed from arm-level data. Abstract-only evidence is explicitly surfaced
as a limitation. Study metadata enrichment is null-preserving; conflicting
non-null stable identity is never silently merged.

## Quantitative audit

M17 fixed/common-effect results remain inverse-variance deterministic. M18 Q,
df and I² semantics remain unchanged. M19 REML estimates tau² only. M22 random
effects reuses that tau² and computes weights/pool; M23 canonical HKSJ and M24
prediction interval are deterministic result objects passed into
`SynthesisContext`. The synthesizer prompt formats these values; it is not asked
to calculate them. Ordering is stabilized by Study/Evidence IDs and group keys.
No persistence or report API snapshot exists for quantitative results; this is
an explicit remaining limitation, not a hidden capability.

## Runtime/API/frontend audit

The queue uses lease version fencing on renew/save/fail/release and clears
terminal leases. Health readiness is PostgreSQL-only and never calls OpenAI or
PubMed. CORS is opt-in from `Cors:AllowedOrigins`; no wildcard is configured by
default. Error responses avoid raw 500 exception details; validation responses
currently expose exception messages, so production deployments should treat
those messages as bounded client-facing diagnostics. The frontend uses generated
API types, tests missing identifiers as absent, renders persisted citations, and
does not perform scientific calculations.

## Test and evidence-quality audit

High-value negative coverage exists for cross-run corpus contamination,
cross-run report citation projection, stale lease ownership, provider identity
conflict, missing source material, malformed/hostile LLM drafts, deterministic
quantitative edge cases, duplicate discovery paths, and fake full-pipeline
execution. PostgreSQL/Testcontainers tests are skipped locally only when Docker
is unavailable and are strict in CI via `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`.
The F5 source-version concurrency test is PostgreSQL-only by design.

## Remaining risks / exactly one next milestone

1. Same-run composite invariants remain application-enforced; hardening them in
   PostgreSQL would require redundant run keys, composite FKs, or triggers.
2. `EvidenceEvaluation.EvidenceIds` is a PostgreSQL UUID array, not a normalized
   join table, so the database cannot validate each element.
3. Hash integrity is not authenticity; a compromised writer can persist a
   self-consistent content/hash pair.
4. Provider availability, scientific truth, semantic outcome harmonization,
   full-paper completeness, and quantitative persisted snapshots remain out of
   scope.

**Recommended exactly one next milestone:** introduce a versioned, persisted
quantitative synthesis artifact/read model, including report/API projection and
reproducibility metadata, without adding a new provider or inference method.

## Локальная верификация

- `dotnet build MedResearch.slnx --configuration Release`: passed, 0 warnings/errors.
- .NET tests: Domain `26 passed/0 failed/0 skipped`; Application
  `160/0/0`; Infrastructure `65/0/0`; Integration `17 passed/0 failed/69
  skipped`; total `268 passed/0 failed/69 skipped`.
- Integration skips are Docker-backed PostgreSQL/Testcontainers tests only;
  local Docker reports the expected missing `dockerDesktopLinuxEngine` pipe.
- `dotnet ef migrations has-pending-model-changes`: passed.
- `docker compose config`: passed.
- Frontend `pnpm test`: API 9 passed, web 18 passed; frontend lint, typecheck,
  and web production build passed.
- `git diff --check`: passed.

CI status and final commit are recorded in the release commit/CI follow-up after
the push; the audit does not treat local skipped PostgreSQL tests as runtime
confidence.
