# Milestone 21: архитектурная верификация и аудит покрытия

Дата: 2026-09-21

## Стартовая точка

Фактический стартовый HEAD: `4d726e4309f94b0d2b2f74aeb7834f18c0bc059b` (`docs: add architecture comprehension guide`).

Ветка: `main`, tracking `origin/main`.

Remote: `https://github.com/DanDan919/MedResearch.git`.

Рабочее дерево перед изменениями было чистым. Предыдущий успешный CI: `35560457417`.

## Цель аудита

Milestone 21 не добавлял научных возможностей. Целью было проверить утверждения архитектурной документации после M20 и усилить тесты там, где зеленый статус мог давать ложную уверенность.

Проверяемые границы:

- `Study` является глобальной идентичностью публикации, а не результатом конкретного поиска.
- `LiteratureSearch` и `ResearchStudyDiscovery` сохраняют provenance конкретного источника и запроса.
- `Evidence`, `EvidenceExtraction`, `EvidenceEvaluation` и `ResearchReport` остаются scoped к `ResearchRun`.
- Синтез использует только current-run Evidence и авторитетную citation projection из persisted `Study`.
- LLM output не является authority для PMID/DOI/StudyId и количественных расчетов.
- Worker lease owner + lease version защищают progress/failure/heartbeat/release операции от stale worker.
- Количественные REML tau-squared значения остаются transient read-model data и не превращаются в random-effects pooling.

## Что было прочитано и сверено

Документация:

- `AGENTS.md`
- `README.md`
- `ARCHITECTURE.md`
- `docs/architecture-overview.md`
- `docs/request-lifecycle.md`
- `docs/learning-path.md`
- `docs/development/current-state.md`
- `docs/development/problems.md`
- `docs/development/discoveries.md`
- `docs/development/technical-debt.md`

Ключевой код:

- API endpoints и health checks в `src/MedResearch.Api/Program.cs`.
- Domain lifecycle и lease metadata в `ResearchRun`.
- Worker orchestration в `BackgroundResearchWorker`, `ResearchRunProcessor`, `ScientificResearchStageExecutor`.
- PostgreSQL queue claim/reclaim/fencing в `PostgreSqlResearchRunQueue`.
- Search orchestration и persistence в `ScientificLiteratureSearchCoordinator` и `EfScientificSearchResultStore`.
- SourceMaterial, EvidenceExtraction, EvidenceEvaluation и ResearchReport persistence stores.
- Corpus validation, synthesis prompt/validator и quantitative read-model builders.
- Provider tests and deterministic fake HTTP/LLM tests.

## Матрица проверенных гарантий

| Инвариант | Реальная реализация | Покрытие до M21 | Изменение M21 |
| --- | --- | --- | --- |
| Invalid `ResearchRun` transitions rejected | Domain methods allow only explicit lifecycle path and terminal-state guards | Domain matrix existed | No change |
| Stale worker cannot save progress after lease transfer | SQL checks `processing_lease_owner` and `processing_lease_version` | Covered for `SaveProgressAsync` | Added heartbeat/failure/release stale-owner tests |
| Terminal runs are not reclaimed | Claim SQL only includes queued/recoverable active statuses | Covered | No change |
| Same study from multiple searches preserves multiple discovery paths | Uniqueness is `(literature_search_id, study_id)` | Covered | No change |
| Same study from multiple sources produces one extraction work item | Extraction/source acquisition group by distinct Study per run | Covered | No change |
| No title-based unsafe merge for no-ID studies | Store creates separate Study rows when stable identifiers are absent | Covered | No change |
| Hard stable-identifier conflicts are skipped rather than merged | Store detects multiple matched persisted Study ids | Covered | No change |
| Completed report citations use authoritative persisted Study metadata | Report read model joins claim -> Evidence -> Study | Covered | Enabled previously unexecuted insufficient-evidence report test |
| Corpus rejects cross-run Evidence | `EvidenceCorpusBuilder` validates `ResearchRunId` | Covered | No change |
| Corpus rejects broken extraction/source lineage | `EvidenceCorpusBuilder` validates extraction/source links | Partially covered | Added missing evidence/extraction lineage test |
| Corpus rejects cross-run search provenance | `EvidenceCorpusBuilder` validates search run scope | Not directly covered | Added search provenance negative test |
| LLM-supplied citation identifiers rejected | `ResearchReportDraftValidator` rejects PMID/DOI/StudyId in claims | Covered | No change |
| Fixed-effect quantitative synthesis remains deterministic read model | Application synthesizer computes values in C# | Covered | No change |
| REML tau-squared foundation does not add random-effects pooling | Only `BetweenStudyVarianceEstimate` is projected | Covered | No change |

## Defects and gaps found

### Средняя важность: integration test existed but was not executed

`ResearchReportStoreTests.PersistReportAsync_PreservesInsufficientEvidenceReportWithoutClaims` lacked `[SkippableFact]`. This meant the repository contained an intended PostgreSQL test for insufficient-evidence report persistence, but xUnit did not discover it.

Fix: added `[SkippableFact]`.

Why it matters: insufficient-evidence reports are the deterministic no-evidence path. This path must be verified against PostgreSQL, because it stores a report without claims.

### Низкая важность: stale lease fencing had asymmetric negative coverage

`SaveProgressAsync` had stale-owner coverage, but heartbeat, failure marking, and release used the same owner/version fencing without direct negative tests.

Fix: added PostgreSQL integration tests for:

- stale owner cannot renew a newer lease;
- stale owner cannot mark a reclaimed run failed;
- stale owner cannot release/clear a newer lease.

### Низкая важность: corpus validation claims had missing direct negative tests

`EvidenceCorpusBuilder` already rejected broken extraction/source/search scope, but some paths were not directly tested.

Fix: added Application tests for:

- Evidence referencing a missing/non-grounded extraction lineage;
- Search provenance from another `ResearchRun`.

## Что не менялось

- Production pipeline behavior.
- Database schema and migrations.
- PubMed, Europe PMC, OpenAI, source acquisition, extraction, evaluation, synthesis providers.
- Fixed-effect pooled result semantics.
- Q/df/I² semantics.
- REML tau² estimator semantics.
- Docker/Compose behavior.

## Локальная верификация на момент аудита

До изменений:

- `dotnet restore E:\MedResearch\MedResearch.slnx`: passed.
- `dotnet build E:\MedResearch\MedResearch.slnx --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test E:\MedResearch\MedResearch.slnx --no-build`: Domain 25 passed; Application 130 passed; Infrastructure 65 passed; Integration 9 passed, 58 skipped.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes.
- `docker compose config`: passed.
- `docker info`: failed locally because Docker Desktop Linux engine pipe was unavailable.

После тестовых изменений, targeted local verification:

- `dotnet build E:\MedResearch\MedResearch.slnx --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test tests\MedResearch.Application.Tests\MedResearch.Application.Tests.csproj --no-build --filter EvidenceCorpusBuilderTests`: 5 passed, 0 failed, 0 skipped.
- `dotnet test tests\MedResearch.IntegrationTests\MedResearch.IntegrationTests.csproj --no-build --filter "ResearchReportStoreTests|ResearchRunQueueConcurrencyTests"`: 23 skipped locally because Docker-backed PostgreSQL is unavailable. The newly added tests were discovered by xUnit.

Финальная локальная проверка перед commit:

- `dotnet restore E:\MedResearch\MedResearch.slnx`: passed.
- `dotnet build E:\MedResearch\MedResearch.slnx --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test E:\MedResearch\MedResearch.slnx --no-build`: Domain 25 passed; Application 132 passed; Infrastructure 65 passed; Integration 9 passed, 62 skipped.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes.
- `docker compose config`: passed.
- `git diff --check`: passed.
- `docker info`: failed locally because Docker Desktop Linux engine pipe was unavailable.

PostgreSQL/Testcontainers execution remains authoritative in CI because local Docker is unavailable.

## Итог архитектурного аудита

M20 documentation broadly matches the current code. The strongest already-verified guarantees are run-scoped Evidence/report persistence, multi-source discovery provenance, identifier-conflict handling, source-material lineage, stale worker fencing, and deterministic quantitative read models.

The main correction from M21 is not an architecture rewrite. It is a coverage correction: one intended integration test was not running, and a few important negative lease/corpus cases now have explicit tests.

## Оставшиеся риски

- PostgreSQL-specific M21 tests cannot execute locally until Docker Desktop Linux engine is available.
- CI must run the required-Docker suite to prove the new PostgreSQL tests execute for real.
- Live provider behavior remains outside normal CI by design.
- REML tau² remains a between-study variance foundation only; no random-effects weights or pooled estimates are implemented.
- Source metadata can still be incomplete or contradictory, and the system intentionally uses conservative non-overwrite/skip behavior rather than fuzzy merge.
