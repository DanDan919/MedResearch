# F14: полный release-candidate аудит MedResearch

Дата аудита: 2026-10-05
Репозиторий: `E:\MedResearch`
Remote: `https://github.com/DanDan919/MedResearch.git`
Ветка: `main`
Фактический baseline до F14: `8b57c6efbec257711246f978a1e6032461b51a82`
Предыдущий подтверждённый CI: [37272462150](https://github.com/DanDan919/MedResearch/actions/runs/37272462150)
Post-F14 CI: [37279009249](https://github.com/DanDan919/MedResearch/actions/runs/37279009249), success

## 1. Метод и честность результата

Это adversarial-аудит существующей системы, а не подтверждение milestone
summary. Код является источником истины; README, `ARCHITECTURE.md`, ADR и
development-документы использованы как вспомогательные свидетельства.

Классификация:

- **VERIFIED** — гарантия видна в production-коде и подтверждена релевантным тестом;
- **PARTIAL** — гарантия действует на application/store boundary, но не является
  универсальной database/deployment гарантией либо имеет явное ограничение;
- **TEST_ONLY** — защита доказана тестом/fixture, но не enforced на runtime boundary;
- **DOC_ONLY** — утверждение найдено в документации, но не подтверждено кодом;
- **STALE** — старое утверждение больше не соответствует коду;
- **UNKNOWN** — в репозитории недостаточно доказательств;
- **CONTRADICTED** — код нарушает заявленное свойство;
- **HALLUCINATED** — заявленное свойство не найдено ни в коде, ни в тестах.

В F14 найден один подтверждённый runtime-дефект и исправлен узко: CORS
middleware выполнялся после auth middleware. Добавлен порядок CORS перед auth и
регрессионный `OPTIONS`-тест. Научные алгоритмы, схема БД и pipeline contract не
перепроектировались.

`CODEX_CONTEXT.md` в baseline отсутствует. Это зафиксировано как отсутствие
файла, а не как причина придумывать дополнительные правила.

## 2. Фактическая карта системы

```text
POST /api/research
  -> Program.cs endpoint group + AuthenticatedUser policy
  -> CreateResearchUseCase
  -> ICurrentActor.RequireSubjectId()
  -> EfResearchStore
  -> ResearchQuestion(owner) + ResearchRun(Queued)
  -> BackgroundResearchWorker
  -> PostgreSqlResearchRunQueue
  -> ResearchRunProcessor
  -> ScientificResearchStageExecutor
     Planning    -> ResearchPlanner -> IResearchPlanStore -> ResearchPlan
     Searching   -> ScientificLiteratureSearchCoordinator
                    -> PubMed / Europe PMC
                    -> IScientificSearchResultStore
                    -> Study + LiteratureSearch + Discovery
     Extracting  -> SourceMaterialAcquirer -> ISourceMaterialStore
                    -> EvidenceExtractor -> IEvidenceExtractionStore
     Evaluating  -> EvidenceEvaluator -> IEvidenceEvaluationStore
     Synthesizing-> SynthesisContextBuilder
                    -> EvidenceCorpusBuilder
                    -> deterministic quantitative pipeline
                    -> IQuantitativeSynthesisArtifactStore
                    -> ResearchSynthesizer -> IResearchReportStore
GET /api/research/{id}/report|quantitative|provenance
```

Application знает provider-neutral contracts. NCBI/Europe PMC XML/JSON,
`HttpClient`, EF Core и PostgreSQL SQL находятся в Infrastructure. API остаётся
composition root, mapping и HTTP/auth boundary.

## 3. Project boundaries

| Граница | Результат | Evidence |
|---|---|---|
| Domain -> Application/Infrastructure/API | VERIFIED | `MedResearch.Domain.csproj`, source scan |
| Application -> только Domain внутри solution | VERIFIED | project references, `ArchitectureBoundaryTests` |
| Application не знает DbContext/DbSet/Npgsql/HTTP/provider DTO | VERIFIED | references/source scan |
| Infrastructure владеет EF/PostgreSQL/HTTP adapters | VERIFIED | `AddInfrastructure`, EF stores, provider adapters |
| API не содержит scientific persistence logic | VERIFIED | `Program.cs` maps endpoints/use cases |
| Optional live projects не входят в `MedResearch.slnx` | VERIFIED | solution inspection |
| Direct SQL bypasses application authorization/fence | PARTIAL | deployment DB credentials are trusted; no RLS/triggers |

## 4. Lifecycle и ResearchRun

Фактическая основная цепочка:

```text
Queued -> Planning -> Searching -> Extracting -> Evaluating -> Synthesizing -> Completed
```

Terminal paths: `Failed`, `Cancelled`. Domain methods закрывают произвольные
переходы, не дают менять `Status` напрямую и очищают lease в terminal state.
Processor переводит состояние только после успешного завершения текущей стадии.

| Атака | Результат |
|---|---|
| `Completed -> Searching` | VERIFIED: domain invalid-transition tests |
| `Failed -> Planning` | VERIFIED: terminal guard |
| `Cancelled -> Extracting` | VERIFIED: terminal guard |
| `Queued -> Evaluating` | VERIFIED: required-current-status guard |
| `Searching -> Completed` | VERIFIED: `Complete` requires `Synthesizing` |
| Recovery changes status arbitrarily | PARTIAL: reclaim resumes persisted active status by queue policy, not Domain transition |
| Failure reason is safe generic text | VERIFIED for worker safe-failure path |
| User can cancel through current public API | UNKNOWN: domain capability exists, public cancellation command/endpoint not present |

`StartedAt`, `CompletedAt`, lease expiry and heartbeat are persisted. Progress
read model is observational and does not fabricate percentages or ETA.

## 5. Authentication, ownership и CORS

Research routes are under `AuthenticatedUser`; health routes are anonymous.
Production default is JWT Bearer with required authority/audience. The
`DevelopmentLocal` scheme is startup-rejected outside `Development`.
`sub` is normalized as an opaque bounded subject and persisted on
`ResearchQuestion.OwnerSubjectId`; clients cannot submit an owner id.

| Guarantee | Status |
|---|---|
| Anonymous research endpoints rejected | VERIFIED: API tests |
| Cross-user run/list/progress/report/quantitative/provenance isolation | VERIFIED: API + PostgreSQL tests |
| Nonexistent and foreign resource use 404-style behavior after auth | VERIFIED for current read paths |
| JWT issuer/audience/signature/lifetime validation configured | VERIFIED by code; external IdP runtime not exercised |
| Token issuance/refresh/session lifecycle | UNKNOWN / out of scope |
| CSRF for bearer transport | PARTIAL: no cookie auth; deployment client/session policy absent |
| Per-user quota/rate limit | NOT IMPLEMENTED; documented debt |
| CORS allow-list | VERIFIED after F14 fix |
| CORS preflight before protected endpoint auth | VERIFIED: `AllowedCorsPreflight_IsHandledBeforeProtectedEndpointAuthorization` |

F14 fix: `UseCors("Frontend")` now precedes `UseAuthentication()` and
`UseAuthorization()`. This does not make research endpoints anonymous; it only
allows an approved browser preflight to complete before the actual protected
request.

## 6. Worker recovery and stale-writer analysis

`BackgroundResearchWorker` creates a scope per iteration and an operational
worker id (`machine + random instance`). `PostgreSqlResearchRunQueue` performs
claim/reclaim in one PostgreSQL transaction using `FOR UPDATE SKIP LOCKED`.
Eligibility is `Queued` or active Planning/Searching/Extracting/Evaluating/
Synthesizing with an absent/expired lease. Reclaim keeps the current stage.

Lease ownership is `(ResearchRunId, ProcessingLeaseOwner,
ProcessingLeaseVersion)`. Reclaim increments the version. Renew, progress,
failure and release updates require owner + version. Stage stores use scoped
`IResearchRunWriteFence`; the fence checks owner/version/expiry/status and locks
the run row inside the stage write transaction. It is attached by the processor
and registered by production DI.

| Scenario | Result |
|---|---|
| worker B claims before expiry | VERIFIED: PostgreSQL tests |
| expired run reclaimed at current stage | VERIFIED |
| completed/failed/cancelled reclaim | VERIFIED: excluded by status |
| heartbeat extends lease | VERIFIED |
| two workers claim different runs | VERIFIED |
| only one worker reclaims one expired run | VERIFIED |
| stale progress/failure/release | VERIFIED: owner/version predicates |
| stale stage output after transfer | VERIFIED: write fence + PostgreSQL regression tests |
| open DB transaction across provider/LLM call | VERIFIED absent from processor/stores |
| direct SQL bypass of fence | PARTIAL: trusted deployment boundary, no DB trigger/RLS |
| transient DB error during heartbeat distinguished from lease transfer | PARTIAL: no dedicated failure taxonomy test; safe operational handling should be revisited |
| shutdown marked as scientific failure | VERIFIED: host cancellation releases/abandons lease and rethrows |

The current processor starts a heartbeat task for each stage and cancels the
stage when the heartbeat loses ownership. A generic non-lease heartbeat
exception is not modeled separately from a stage failure; this is a real
remaining operational risk, but not a reason to invent a scheduler.

## 7. Pipeline contracts

| Stage | Input | Output | Untrusted data | Deterministic gate | Retry/idempotency |
|---|---|---|---|---|---|
| Planning | question | ResearchPlan | structured LLM | question/query/type/length/identifier validator | one plan per run; equivalent retry |
| Searching | plan queries | searches, studies, discoveries | provider HTTP/metadata | normalization, identity conflict handling, bounded source results | search execution key + DB uniqueness |
| Extracting | distinct discovered Studies + selected SourceMaterial | Extraction + Evidence | source text + LLM draft | excerpt containment, numeric token presence, enum/bounds | run/study/source/prompt idempotency |
| Evaluating | current-run extraction/evidence | Evaluation | LLM categorical draft | source-scope/authoritative ids/unknown states | run/study/prompt idempotency |
| Synthesizing | validated current-run corpus | report + claims | LLM narrative/claims | EvidenceId-only citations, direction, count, same-run lineage | report prompt idempotency + FK/application validation |

Provider zero-results are distinct from provider exceptions in coordinator
control flow, but a failed attempt before persistence has no first-class
`LiteratureSearch` row. This is **PARTIAL**, not invented as a success.

## 8. Scientific identity and provenance

`Study` is global publication identity; `ResearchRun`, Evidence, Extraction,
Evaluation, Report and quantitative artifacts are run-scoped. Stable identifier
normalization supports PMID, PMCID and DOI. Title/author/year fuzzy merging is
absent. Hard conflicts where identifiers resolve to different Studies are
skipped/logged and do not merge or overwrite entities.

Expected and verified multi-source graph:

```text
Run X
  Search PubMed       -> Discovery -> Study X
  Search Europe PMC   -> Discovery -> Study X
```

This yields one canonical Study, separate LiteratureSearch rows, separate
ResearchStudyDiscovery rows, and one distinct downstream Study work item per
run for the normal extraction selection.

Concurrency uses PostgreSQL advisory transaction locks for stable identity keys
and filtered unique indexes as a final persistence guard. No-ID records are not
merged by title.

| Identity case | Status |
|---|---|
| same PMID/DOI | VERIFIED |
| same PMID from two sources | VERIFIED |
| PMID matches A, DOI matches B | VERIFIED: hard conflict skipped |
| PMCID matches A, PMID matches B | VERIFIED: hard conflict skipped |
| null incoming metadata | VERIFIED: does not erase rich metadata |
| different non-null metadata on same Study | PARTIAL: conservative existing value is retained, but conflict is not persisted as a structured diagnostic |
| all stable ids null | VERIFIED: no automatic fuzzy merge |
| every provider attempt persisted | PARTIAL: failed attempts remain logs, `hasPersistedProviderFailureProvenance=false` |
| every global SourceMaterial snapshot attributed to selected run | PARTIAL: SourceMaterial is global per Study; extraction keeps exact selected id |

## 9. LLM trust boundary and hallucination audit

All structured LLM output is untrusted. Planner, extractor, evaluator and
synthesizer have strict schemas and application validators. Validation-guided
repair is bounded (default one semantic repair, maximum configured two) and
uses a complete replacement; non-repairable cross-run/identity issues fail
closed. Codex CLI is development/manual-only and runs read-only with project
credentials removed from the child environment.

Verified anti-hallucination controls:

- planner cannot invent authoritative PMID/DOI into accepted queries;
- extractor does not run when source text is absent;
- supporting excerpt must occur in supplied SourceMaterial;
- numeric fields are retained only when the same token occurs in source text;
- evaluation preserves Unknown/InsufficientSource/NotApplicable distinctions;
- synthesis rejects model PMID/DOI/StudyId authority;
- report claims cite only supplied EvidenceIds;
- evidence/report/artifact lineage requires current run, Study and exact source
  material relationships.

Important non-guarantees:

- substring numeric grounding does not prove that the token is the value of the
  intended statistic;
- excerpt containment does not prove scientific interpretation or causality;
- evaluator labels are not formal GRADE/RoB/ROBINS-I/AMSTAR-2 results;
- prompt instructions alone cannot make hostile source text harmless;
- narrative report text is not independently reconstructed from numeric artifacts.

These are **PARTIAL**, not `VERIFIED scientific truth` claims. Numeric semantic
grounding is intentionally future work and is not implemented in F14.

## 10. Report, quantitative and provenance read models

Report persistence validates grounded completed same-run Evidence -> Extraction ->
SourceMaterial -> Study lineage before inserting claim links. The report read
projection re-resolves authoritative PMID/PMCID/DOI/title/journal metadata from
Study rather than trusting the model.

Quantitative artifacts are deterministic Application results. Fixed-effect,
REML random-effects Wald, canonical HKSJ and prediction interval calculations
are not performed by the LLM. Artifact persistence stores JSON plus relational
contribution snapshots, fingerprint, exact lineage ids, method rows and ordinals;
read validation compares the JSON and relational representation.

Frontend quantitative rendering is read-only: it fetches by `researchRunId`,
validates response shape with Zod, displays persisted values and does not run
scientific formulas. The former stale UI phrase “not available in F6” was
removed; it now says “this artifact”.

| Property | Status |
|---|---|
| report claims cite real Evidence | VERIFIED |
| citation projection resolves canonical Study metadata | VERIFIED |
| provenance omits raw SourceMaterial.Content | VERIFIED |
| provenance filters run-scoped extraction/evidence/evaluation/report links | VERIFIED |
| failed-provider attempt shown as persisted provenance | PARTIAL / explicitly false flag |
| artifact lineage exact source material | VERIFIED |
| artifact id/fingerprint linked to ResearchReport by persisted FK | PARTIAL: logical same-call relationship only |
| same-run citation invariant is a pure PostgreSQL FK | PARTIAL: application/store validation, no redundant run id in join |
| numeric semantic grounding | PARTIAL / out of scope |
| frontend computes HKSJ/Wald/tau/Q/I²/prediction | VERIFIED false; it displays API artifacts |
| frontend invents study CI/bibliographic metadata | VERIFIED false in current code/tests |

## 11. API/OpenAPI/frontend consistency

The API routes and generated frontend client cover run history, details,
progress, report, quantitative artifacts and provenance. Zod schemas validate
runtime JSON, query keys include `researchRunId`, and frontend tests use fake
responses rather than live providers. Playwright covers protected/unauthenticated
states and report/quantitative/provenance views.

| Concern | Result |
|---|---|
| API client adds bearer token centrally | VERIFIED |
| no frontend secret persistence | VERIFIED by source/config inspection |
| query cache cross-run contamination | VERIFIED prevented by scoped query keys |
| OpenAPI/generated client drift | VERIFIED for current generated-diff CI check |
| frontend is an authorization boundary | CONTRADICTED if assumed; server auth remains authoritative |
| browser preflight for allowed protected route | VERIFIED after F14 fix |
| exact UX for unknown/unauthorized/error states | VERIFIED by API/web/Playwright tests; not a security proof |

## 12. CI and test confidence

CI workflow `.github/workflows/ci.yml` has two Ubuntu jobs. Frontend runs
install, generated API diff, lint, typecheck, unit tests, Chromium/Playwright
and builds. Backend sets `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`, runs real
PostgreSQL/Testcontainers integration tests, rejects unexpected skips, checks
EF pending migrations and validates Compose. It does not require OpenAI, NCBI
credentials or live PubMed/Europe PMC.

Before F14, run `37272462150` was green. During F14 local Docker Desktop became
available and the full local .NET suite executed without PostgreSQL skips:

| Project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Domain | 28 | 0 | 0 |
| Application | 176 | 0 | 0 |
| Infrastructure | 73 | 0 | 0 |
| Integration/PostgreSQL | 105 | 0 | 0 |
| Total | 382 | 0 | 0 |

Additional local checks: restore passed, build passed with 0 warnings/0 errors,
`dotnet ef migrations has-pending-model-changes` passed, `docker compose config`
passed, `docker info` passed, `git diff --check` passed. Frontend full F7/F13
baseline had API 14, web 22, Playwright 10, lint/typecheck/build green; F14
also changed only stale display text and requires a fresh frontend run in CI.

No live OpenAI, PubMed, Europe PMC, Codex CLI or paid network call was used by
the normal validation. Live projects remain opt-in and outside the solution.
Post-F14 CI `37279009249` completed successfully on `5fb285ca`; both Ubuntu
jobs passed, including the strict Docker/Testcontainers gate, EF model check,
Compose validation, frontend Playwright and production builds. The standard
workflow does not run live PubMed/Europe PMC/OpenAI checks.

## 13. Findings and actions

### F14-01, MEDIUM, fixed: CORS preflight ordering

**Problem:** CORS middleware followed auth middleware, so an allowed browser
preflight for a protected route could be rejected before CORS headers were added.

**Fix:** move `UseCors("Frontend")` before authentication/authorization.

**Regression:** `AllowedCorsPreflight_IsHandledBeforeProtectedEndpointAuthorization`.

**Result:** VERIFIED after fix.

### F14-02, MEDIUM, not fixed: no first-class provider-failure provenance

**Problem:** successful zero-result searches are persisted, but an attempt that
fails before persistence is only operationally logged.

**Reason deferred:** needs a deliberate provenance/attempt schema and migration;
it is not safe to fabricate a failed LiteratureSearch row with current model.

**Result:** PARTIAL, explicitly documented by coverage flag and technical debt.

### F14-03, MEDIUM, not fixed: global SourceMaterial attribution

**Problem:** SourceMaterial snapshots are global per Study, while acquisition
attempts are not run-scoped. Exact extraction source id is retained, but the API
cannot prove that every global snapshot was fetched during this run.

**Result:** PARTIAL; no false provenance claim is made.

### F14-04, MEDIUM, not fixed: semantic numeric grounding

**Problem:** source-token presence is weaker than proving that the token belongs
to the correct statistic/CI/p-value field.

**Result:** PARTIAL and intentionally outside F14; no F15 functionality added.

### F14-05, LOW, fixed: stale frontend milestone label

**Problem:** quantitative UI described current artifact limitations as “F6”.

**Fix:** neutral current-artifact wording.

**Result:** VERIFIED stale-claim removal.

## 14. Classification summary

| Class | Examples |
|---|---|
| VERIFIED | layer direction, auth ownership, lifecycle, queue atomicity, fencing, report citations, quantitative read-only UI, CI skip gate |
| PARTIAL | direct SQL bypass, provider failure provenance, global SourceMaterial acquisition, numeric semantic grounding, report/artifact FK, same-run DB constraint |
| TEST_ONLY | external IdP availability, some provider/live runtime behavior, test-only auth handler |
| DOC_ONLY | none accepted as a runtime guarantee without code evidence |
| STALE | old F6 frontend label; removed in F14 |
| UNKNOWN | refresh/session lifecycle, public Cancel endpoint, deployment TLS/WAF/DB RLS |
| CONTRADICTED | pre-F14 CORS ordering; fixed and regression-tested |
| HALLUCINATED | no claim accepted solely from old milestone summaries |

## 15. Release-candidate decision

**Code audit result: conditional release candidate.** The core runtime and
scientific trust boundaries are coherent and the high-value PostgreSQL tests
execute locally when Docker is available. F14 does not certify scientific truth,
external identity-provider deployment, provider uptime, semantic numeric
grounding, persisted failed-attempt provenance, or database-level RLS.

The release-candidate audit gate is **green** after post-F14 CI `37279009249`.
The remaining PARTIAL items are explicitly bounded architectural debt, not
hidden features to be claimed as complete.

## 16. Audit answers 135--150

135. **Scientific integrity:** grounded Evidence and deterministic artifacts are
   protected; semantic interpretation remains bounded and non-authoritative.
136. **LLM authority:** no; LLM output is validated and never citation authority.
137. **Numeric authority:** deterministic Application code, not LLM narrative.
138. **Source identity:** canonical Study uses normalized stable identifiers.
139. **No-ID policy:** retain separately; no fuzzy merge.
140. **Cross-run Evidence:** rejected by corpus, synthesis, report and artifact checks.
141. **Cross-user access:** protected by auth + owner-scoped stores.
142. **Worker ownership:** owner + lease version + expiry.
143. **Reclaim:** current active stage, not whole-run reset.
144. **Stale worker:** cannot pass queue/store fence after transfer.
145. **Terminal cleanup:** lease metadata cleared.
146. **Provider failures:** logged, not falsely persisted as successful searches.
147. **Zero results:** valid successful search state.
148. **Source content exposure:** raw SourceMaterial content excluded from provenance/read API.
149. **Quantitative UI:** display of persisted artifacts, not recomputation.
150. **F14 conclusion:** RC audit gate green; remaining partial guarantees are
   documented debt, not release claims.

## 17. Files touched by F14

- `src/MedResearch.Api/Program.cs` — CORS middleware order.
- `tests/MedResearch.IntegrationTests/ResearchApiTests.cs` — preflight regression.
- `frontend/apps/web/components/research/quantitative/quantitative-workspace.tsx` — stale label removal.
- `docs/audits/f14-full-system-release-candidate-audit-ru.md` — this audit.
- `docs/development/current-state.md` — audit/current guarantees.
- `docs/development/technical-debt.md` — confirmed remaining limitations.
