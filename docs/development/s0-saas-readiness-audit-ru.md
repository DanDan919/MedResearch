# S0: готовность MedResearch к SaaS, научная проверка и экономика

Дата независимого аудита: 2026-10-10. Продуктовая гипотеза: **MedResearch Evidence Briefs** для medical writers, независимых исследователей и небольших научных команд. Это не диагностическая система и не замена эксперту.

Область работы: чтение текущего репозитория, безопасные локальные проверки, чтение CI и сохранённых артефактов, проектирование будущих проверок. Изменён только этот документ. Production-код, тесты, настройки, зависимости и миграции не изменены. Следующий milestone не начат; commit/push не выполнялись.

## 1. Executive Summary: главный вывод

**INFERRED:** существующий модульный монолит архитектурно пригоден для небольшого контролируемого web-пилота. Переписывать его, вводить tenants для индивидуальных пользователей, брокер сообщений или отдельный кластер worker сейчас не требуется. Вместимость 10–50 пользователей не измерена: число приглашений не равно числу одновременно выполняемых исследований.

**VERIFIED_IN_CODE / VERIFIED_BY_TEST / VERIFIED_BY_CI:** сильные стороны уже существуют: owner-scoped API, PostgreSQL как долговечная очередь, lease/fencing/recovery, два источника литературы, immutable source snapshots, точная научная lineage, проверка локальных числовых связок, детерминированные количественные расчёты, структурированные claims, Next.js BFF и synthetic OIDC release tests.

**NOT_IMPLEMENTED / UNKNOWN:** нет атомарных пользовательских/глобальных квот, учёта токенов и денег, проверенного восстановления из backup и законченного жизненного цикла данных. Нет независимо измеренной экспертной точности текущего pipeline, стоимости полезного отчёта и спроса. Реальный IdP/внешний HTTPS Gate B не проверены.

**Вердикт о запуске сегодня:** локальную демонстрацию на fake fixtures можно показывать с соответствующей маркировкой. Безопасный самостоятельный invite-only сервис с реальными LLM-запросами пока не готов. Минимальные препятствия: ограничение допуска исследований и расходов; безопасный scientific evaluation harness; экспертно проверенная полезность; реальный deployment/auth; минимальные backup/retention/operator процедуры.

Главная научная неопределённость: Verified source tuple не доказывает корректность медицинской интерпретации, независимость когорт и полноту поиска. Главная финансовая неопределённость: фактические tokens/cost отсутствуют, а recovery не имеет общего lifetime-budget. Главный операционный риск: синтетически проверенное приложение ещё не проверено в реальной инфраструктуре и не имеет доказанного restore.

**INFERRED:** сложность scientific validators оправдана угрозами, но автору трудно поддерживать связку anchor normalization, numeric binding, corpus validation, artifact fingerprints и structured claims без карты контрактов. Tauri и enterprise/billing до подтверждения спроса добавят сложность без доказанной пользы.

## 2. Текущий baseline и источники доказательств

### 2.1. Репозиторий

| Проверка | Факт | Класс доказательства |
| --- | --- | --- |
| HEAD при старте | 25667c243b880a71bbbe2c423009886432dfaad3 | VERIFIED_IN_CODE: git rev-parse HEAD |
| Ветка / upstream | main / origin/main | VERIFIED_IN_CODE: git branch, upstream |
| Remote fetch/push | https://github.com/DanDan919/MedResearch.git | VERIFIED_IN_CODE: git remote -v |
| Начальное дерево | Чистое; git status --short пуст | VERIFIED_IN_CODE |
| История | Прочитаны последние 30 commits; последние 25667c2 и d3f305d относятся к F19 | VERIFIED_IN_CODE |
| Финальный HEAD S0 | Тот же; новый отчёт не закоммичен | VERIFIED_IN_CODE |

Никакие reset, restore, clean, stash, force-push или изменения истории не выполнялись.

### 2.2. Локальная проверка именно в S0

SDK 10.0.401, Node.js v24.19.0. Предыдущие указания SDK 10.0.400 не перенесены автоматически.

| Проверка | Passed | Failed | Skipped | Примечание |
| --- | ---: | ---: | ---: | --- |
| Domain | 45 | 0 | 0 | Новый локальный запуск |
| Application | 289 | 0 | 0 | Новый локальный запуск |
| Infrastructure | 110 | 0 | 0 | Новый локальный запуск |
| Integration | 52 | 0 | 103 | PostgreSQL/Testcontainers недоступны локально |
| Backend total | 496 | 0 | 103 | 599 обнаруженных tests |
| Frontend API | 40 | 0 | 0 | pnpm test |
| Frontend Web | 99 | 0 | 0 | pnpm test |
| Deployment preflight | 40 | 0 | 0 | Fake HTTP; не внешний deployment |
| Security policy | 5 | 0 | 0 | Offline policy tests; не свежий registry audit |

VERIFIED_BY_TEST: dotnet build MedResearch.slnx --no-restore прошёл без warnings/errors; dotnet test выполнен с --no-build --no-restore, TRX в E:/MedResearch/TestResults/S0. MEDRESEARCH_UPDATE_OPENAPI=false исключал регенерацию baseline. UI/desktop packages с --passWithNoTests не имеют собственного unit coverage; это не дополнительные passed tests.

VERIFIED_BY_TEST: EF has-pending-model-changes --no-build с Infrastructure project и Api startup сообщает отсутствие изменений модели; docker compose config --quiet проходит. Docker info завершился ошибкой отсутствующего dockerDesktopLinuxEngine pipe. Docker Desktop не ремонтировался.

Новый restore/install, live scientific test, browser/load/native release test и внешнее сканирование зависимостей в S0 не выполнялись. Это не повторная проверка всего F18/F19 runtime на локальной машине.

### 2.3. CI: проверено независимо от прежнего сообщения

[GitHub Actions run 37934194980](https://github.com/DanDan919/MedResearch/actions/runs/37934194980): API GitHub показывает completed/success и точный HEAD 25667c243b880a71bbbe2c423009886432dfaad3, 2026-10-09. Jobs Frontend, Build and test, Actual API PostgreSQL HTTPS web RC завершились успешно.

VERIFIED_BY_CI: прочитаны сохранённые TRX из E:/MedResearch/TestResults/F19/final-backend: Domain 45, Application 289, Infrastructure 110, Integration 155; всего **599 passed, 0 failed, 0 skipped**. Integration включает 103 Docker-backed случая, пропущенных локально. В S0 имена всех 103 local NotExecuted дополнительно сопоставлены с CI Passed: совпали 103/103, unmatched0. Strict MEDRESEARCH_REQUIRE_DOCKER_TESTS=true и проверка TRX в workflow не допускают скрытых обязательных skips.

VERIFIED_BY_CI: сохранённый frontend-final-ci.log подтверждает API 40, Web 99, policy 5, preflight 40, scientific Playwright 15, production synthetic OIDC 17. E:/MedResearch/TestResults/F19/final-web/fullstack-results.json: 17 expected, 0 unexpected/skipped/flaky. CI собирает frontend. Runner ubuntu-latest, .NET 10.0.x, Node 24, pnpm 11.19.0; Docker доступен.

Не складывать эти suites в одну метрику scientific accuracy. Actual API + PostgreSQL + trusted temporary HTTPS + synthetic IdP не означает real external IdP/deployment или живую научную проверку. Native Tauri build не подтверждён.

### 2.4. Карта проверенных источников

Ссылки ниже используются как идентификаторы E01–E21. Названия методов указаны в соответствующих разделах; путь должен вести к существующему файлу, а не к предполагаемому модулю.

| ID | Источник |
| --- | --- |
| E01 | [AGENTS.md](E:/MedResearch/AGENTS.md), [README.md](E:/MedResearch/README.md), [ARCHITECTURE.md](E:/MedResearch/ARCHITECTURE.md) |
| E02 | [current-state.md](E:/MedResearch/docs/development/current-state.md), [technical-debt.md](E:/MedResearch/docs/development/technical-debt.md), [problems.md](E:/MedResearch/docs/development/problems.md) |
| E03 | [Program.cs](E:/MedResearch/src/MedResearch.Api/Program.cs), [ResearchApiModels.cs](E:/MedResearch/src/MedResearch.Api/Research/ResearchApiModels.cs), [CreateResearchUseCase.cs](E:/MedResearch/src/MedResearch.Application/Research/CreateResearchUseCase.cs), [EfResearchStore.cs](E:/MedResearch/src/MedResearch.Infrastructure/Research/EfResearchStore.cs) |
| E04 | [MedResearchDbContext.cs](E:/MedResearch/src/MedResearch.Infrastructure/Persistence/MedResearchDbContext.cs), [Infrastructure DI](E:/MedResearch/src/MedResearch.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs), [appsettings.json](E:/MedResearch/src/MedResearch.Api/appsettings.json) |
| E05 | [BackgroundResearchWorker.cs](E:/MedResearch/src/MedResearch.Infrastructure/Research/Processing/BackgroundResearchWorker.cs), [ResearchRunProcessor.cs](E:/MedResearch/src/MedResearch.Application/Research/Processing/ResearchRunProcessor.cs), [PostgreSqlResearchRunQueue.cs](E:/MedResearch/src/MedResearch.Infrastructure/Research/Processing/PostgreSqlResearchRunQueue.cs), [PostgreSqlResearchRunWriteFence.cs](E:/MedResearch/src/MedResearch.Infrastructure/Research/Processing/PostgreSqlResearchRunWriteFence.cs) |
| E06 | [ScientificResearchStageExecutor.cs](E:/MedResearch/src/MedResearch.Application/Research/Processing/ScientificResearchStageExecutor.cs), [ResearchPlanner.cs](E:/MedResearch/src/MedResearch.Application/Research/Planning/ResearchPlanner.cs) |
| E07 | [ScientificLiteratureSearchCoordinator.cs](E:/MedResearch/src/MedResearch.Application/Research/Literature/ScientificLiteratureSearchCoordinator.cs), [EfScientificSearchResultStore.cs](E:/MedResearch/src/MedResearch.Infrastructure/Literature/Persistence/EfScientificSearchResultStore.cs), [ScientificIdentifierNormalizer.cs](E:/MedResearch/src/MedResearch.Infrastructure/Literature/Identity/ScientificIdentifierNormalizer.cs) |
| E08 | [SourceMaterialAcquirer.cs](E:/MedResearch/src/MedResearch.Application/Research/SourceMaterials/SourceMaterialAcquirer.cs), [EfSourceMaterialStore.cs](E:/MedResearch/src/MedResearch.Infrastructure/SourceMaterials/Persistence/EfSourceMaterialStore.cs), [EuropePmcFullTextSourceMaterialProvider.cs](E:/MedResearch/src/MedResearch.Infrastructure/SourceMaterials/EuropePmc/EuropePmcFullTextSourceMaterialProvider.cs) |
| E09 | [EvidenceExtractor.cs](E:/MedResearch/src/MedResearch.Application/Research/Extraction/EvidenceExtractor.cs), [SemanticNumericGroundingVerifier.cs](E:/MedResearch/src/MedResearch.Application/Research/Extraction/SemanticNumericGroundingVerifier.cs), [EfEvidenceExtractionStore.cs](E:/MedResearch/src/MedResearch.Infrastructure/Extraction/Persistence/EfEvidenceExtractionStore.cs) |
| E10 | [EvidenceEvaluator.cs](E:/MedResearch/src/MedResearch.Application/Research/Evaluation/EvidenceEvaluator.cs), [EvidenceEvaluationPrompt.cs](E:/MedResearch/src/MedResearch.Application/Research/Evaluation/EvidenceEvaluationPrompt.cs), [EfEvidenceEvaluationStore.cs](E:/MedResearch/src/MedResearch.Infrastructure/Evaluation/Persistence/EfEvidenceEvaluationStore.cs) |
| E11 | [ResearchSynthesizer.cs](E:/MedResearch/src/MedResearch.Application/Research/Synthesis/ResearchSynthesizer.cs), [SynthesisContextBuilder.cs](E:/MedResearch/src/MedResearch.Application/Research/Synthesis/SynthesisContextBuilder.cs), [EvidenceCorpusBuilder.cs](E:/MedResearch/src/MedResearch.Application/Research/Synthesis/EvidenceCorpusBuilder.cs), [EvidenceNumericProof.cs](E:/MedResearch/src/MedResearch.Application/Research/Synthesis/EvidenceNumericProof.cs) |
| E12 | [QuantitativeEvidenceAssessor.cs](E:/MedResearch/src/MedResearch.Application/Research/Quantitative/QuantitativeEvidenceAssessor.cs), [QuantitativeStatisticalSynthesizerTests.cs](E:/MedResearch/tests/MedResearch.Application.Tests/QuantitativeStatisticalSynthesizerTests.cs), [StructuredNarrativeClaimTests.cs](E:/MedResearch/tests/MedResearch.Application.Tests/StructuredNarrativeClaimTests.cs) |
| E13 | [IStructuredLlmClient.cs](E:/MedResearch/src/MedResearch.Application/Research/Ai/IStructuredLlmClient.cs), [StructuredLlmContracts.cs](E:/MedResearch/src/MedResearch.Application/Research/Ai/StructuredLlmContracts.cs), [ValidationGuidedLlmRepairService.cs](E:/MedResearch/src/MedResearch.Application/Research/Ai/ValidationGuidedLlmRepairService.cs), [OpenAIStructuredLlmClient.cs](E:/MedResearch/src/MedResearch.Infrastructure/Ai/OpenAI/OpenAIStructuredLlmClient.cs), [CodexCliStructuredLlmClient.cs](E:/MedResearch/src/MedResearch.Infrastructure/Ai/CodexCli/CodexCliStructuredLlmClient.cs) |
| E14 | [LiveScientificPipelineE2ETests.cs](E:/MedResearch/tests/MedResearch.LiveE2EValidationTests/LiveScientificPipelineE2ETests.cs), [ADR-015](E:/MedResearch/docs/architecture/decisions/ADR-015-live-scientific-e2e-validation.md) |
| E15 | [JwtApiTests.cs](E:/MedResearch/tests/MedResearch.IntegrationTests/JwtApiTests.cs), [ResearchRunQueueConcurrencyTests.cs](E:/MedResearch/tests/MedResearch.IntegrationTests/ResearchRunQueueConcurrencyTests.cs), [ScientificSearchResultStoreTests.cs](E:/MedResearch/tests/MedResearch.IntegrationTests/ScientificSearchResultStoreTests.cs), [FullFakePipelineTests.cs](E:/MedResearch/tests/MedResearch.IntegrationTests/FullFakePipelineTests.cs) |
| E16 | [Web auth config](E:/MedResearch/frontend/apps/web/lib/auth/config.ts), [session.ts](E:/MedResearch/frontend/apps/web/lib/auth/session.ts), [oidc.ts](E:/MedResearch/frontend/apps/web/lib/auth/oidc.ts), [bff.ts](E:/MedResearch/frontend/apps/web/lib/auth/bff.ts), [web-session-provider.tsx](E:/MedResearch/frontend/apps/web/components/web-session-provider.tsx) |
| E17 | [research-report-workspace.tsx](E:/MedResearch/frontend/apps/web/components/research/research-report-workspace.tsx), [evidence-provenance-workspace.tsx](E:/MedResearch/frontend/apps/web/components/research/evidence-provenance-workspace.tsx), [quantitative-workspace.tsx](E:/MedResearch/frontend/apps/web/components/research/quantitative/quantitative-workspace.tsx), [create-research-form.tsx](E:/MedResearch/frontend/apps/web/components/research/create-research-form.tsx) |
| E18 | [ci.yml](E:/MedResearch/.github/workflows/ci.yml), [pnpm-lock.yaml](E:/MedResearch/frontend/pnpm-lock.yaml), [security-audit-policy.mjs](E:/MedResearch/frontend/scripts/security-audit-policy.mjs), [deployment-preflight.mjs](E:/MedResearch/frontend/scripts/deployment-preflight.mjs) |
| E19 | [F19 report](E:/MedResearch/docs/development/f19-real-https-oidc-deployment-verification-ru.md), [deployment.md](E:/MedResearch/docs/frontend/deployment.md), [real-deployment-verification.md](E:/MedResearch/docs/frontend/real-deployment-verification.md) |
| E20 | [F15.1](E:/MedResearch/docs/development/f15-1-scientific-trust-boundary-correction-ru.md), [F15.2](E:/MedResearch/docs/development/f15-2-provider-runtime-integrity-hardening-ru.md), [F16](E:/MedResearch/docs/development/f16-structured-narrative-claim-grounding-ru.md), [F17](E:/MedResearch/docs/development/f17-production-web-authentication-flow-ru.md), [F18](E:/MedResearch/docs/development/f18-web-release-candidate-ru.md) |
| E21 | [F10](E:/MedResearch/docs/audits/f10-threat-model-and-authorization-ru.md), [F12](E:/MedResearch/docs/development/f12-validation-guided-llm-repair-ru.md), [ADR-025](E:/MedResearch/docs/architecture/decisions/ADR-025-authentication-and-research-ownership.md), [ADR-027](E:/MedResearch/docs/architecture/decisions/ADR-027-validation-guided-llm-repair.md), [ADR-029](E:/MedResearch/docs/architecture/decisions/ADR-029-bound-statistical-tuples-and-trusted-synthesis.md), [ADR-030](E:/MedResearch/docs/architecture/decisions/ADR-030-provider-attempts-and-bounded-responses.md), [ADR-031](E:/MedResearch/docs/architecture/decisions/ADR-031-structured-authoritative-scientific-claims.md), [ADR-032](E:/MedResearch/docs/architecture/decisions/ADR-032-oidc-web-session-and-bff.md) |

Классы доказательств: VERIFIED_IN_CODE = проверен конкретный путь/контракт; VERIFIED_BY_TEST = соответствующая локальная проверка; VERIFIED_BY_CI = соответствующая проверка CI; DOCUMENTED_ONLY = написано, но здесь не воспроизведено; INFERRED = вывод из проверенного поведения; NOT_IMPLEMENTED = отсутствует в проверенных контрактах/маршрутах/schema; UNKNOWN = данных недостаточно. Это не универсальные сертификаты безопасности.

## 3. Проверенная архитектура

### 3.1. Слои и технологии

| Компонент | Реальная ответственность и зависимости | Доказательство |
| --- | --- | --- |
| Domain | Научные сущности и lifecycle; без ссылок на Application/Infrastructure/ASP.NET/EF/HTTP | VERIFIED_IN_CODE; ArchitectureBoundaryTests, VERIFIED_BY_TEST |
| Application | Use cases, provider-neutral contracts, stage coordination, validation/quantitative logic; зависит от Domain и Extensions abstractions | VERIFIED_IN_CODE / VERIFIED_BY_TEST |
| Infrastructure | EF Core 10, Npgsql, PostgreSQL, HTTP sources, OpenAI/Codex adapters, persistence fences, hosted worker | VERIFIED_IN_CODE E04/E05/E13 |
| Api | ASP.NET Core 10 minimal HTTP, auth, transport projection, composition root | VERIFIED_IN_CODE E03 |
| Worker | BackgroundResearchWorker размещён в Infrastructure и запущен внутри API host; не отдельный сервис | VERIFIED_IN_CODE E05 |
| PostgreSQL | Version 17 в Docker/test setup; source of truth, очередь, locks, unique/FK constraints | VERIFIED_IN_CODE / VERIFIED_BY_CI |
| Web | Next.js 16.3.8, React lockfile 19.3.0, TS 5.9.3, TanStack Query, Zod, generated OpenAPI API package, shared UI/lucide | VERIFIED_IN_CODE E17/E18 |
| Desktop | React/Vite shell, Tauri 2/Rust scaffold; native release UNKNOWN | VERIFIED_IN_CODE; web build не native proof |
| Auth | openid-client 6.8.8, iron-session 9.0.1, jose 6.2.12; server BFF, JWT API | VERIFIED_IN_CODE E16/E18 |
| LLM | IStructuredLlmClient; OpenAI production adapter; CodexCli ограничен development/manual | VERIFIED_IN_CODE E13 |
| Literature | PubMed и Europe PMC через IScientificLiteratureSource; source acquisition отдельный контракт | VERIFIED_IN_CODE E07/E08 |

Фактическое направление: Domain ← Application ← Infrastructure; Api связывает Application/Infrastructure. ArchitectureBoundaryTests.DomainProject_HasNoInfrastructureOrApplicationDependencies и ApplicationProject_DependsOnlyOnDomainWithinMedResearch проверяют проектные зависимости. Они не доказывают отсутствие всех возможных semantic leaks.

### 3.2. Исполнение

    POST /api/research
    → CreateResearchUseCase.ExecuteAsync
    → IResearchStore / EfResearchStore.PersistInitialResearchAsync
    → ResearchQuestion + Queued ResearchRun, одна DB transaction
    → BackgroundResearchWorker
    → ResearchRunProcessor.ProcessNextQueuedRunAsync
    → IResearchRunQueue / PostgreSqlResearchRunQueue
    → IResearchRunWriteFence / PostgreSqlResearchRunWriteFence
    → ScientificResearchStageExecutor.ExecuteAsync
    → Planning → Searching → Extracting → Evaluating → Synthesizing
    → persisted ResearchReport → Completed → owner-scoped GET /report

VERIFIED_IN_CODE E03–E11: API не рассчитывает научные эффекты и не перескакивает стадии. Очередь claim/reclaim атомарна через FOR UPDATE SKIP LOCKED. Worker работает последовательно в одном host; запуск нескольких API replicas добавляет несколько workers, а не глобальный лимит нагрузки.

Lease: owner instance, acquired/expires/heartbeat, version. Defaults ResearchProcessing:LeaseDurationSeconds=900, HeartbeatIntervalSeconds=60, IdleDelayMilliseconds=1000. Active expired run возобновляет текущую стадию; terminal states не reclaim. Короткий fenced transaction защищает запись owner/version/valid expiry; DB transaction не держится во время HTTP/LLM.

VERIFIED_BY_CI E15: exclusive claims, competing reclaim, heartbeat, stale progress/failure/renew/release и terminal cases проверяются настоящим PostgreSQL. Это защита tested persisted effects от stale writes и проверенная idempotency по конкретным ключам, **не универсальное exactly-once и не exactly-once для внешнего оплаченного запроса**.

### 3.3. Контракты стадий

| Стадия | Вход → выход / persistence | Внешнее и LLM | Validation, failure, recovery | Ресурсы / метрики |
| --- | --- | --- | --- | --- |
| Planning | Question → accepted ResearchPlan: bounded queries, PICO hints, provider/model/prompt/time | Один GenerateStructuredAsync при отсутствии accepted plan | ResearchPlanValidator; malformed/provider failure → failed run; accepted plan повторно используется | Размер question/schema, одна generation; provenance есть, tokens/cost нет |
| Searching | Plan queries → отдельные LiteratureSearch, provider attempts, canonical Study, Discovery | PubMed ESearch + batched EFetch; Europe PMC bounded pages; без LLM | source/count/ID validation; zero success допустим; failed source сохраняется; при успехе другого source можно продолжить; completed searches повторно используются | queries × sources × pages/batches × HTTP retry; attempts/counts есть, полной HTTP billing ledger нет |
| Extracting | Discovered distinct Study → SourceMaterial acquisition → EvidenceExtraction + grounded Evidence | До выбранного лимита Studies; source HTTP, затем repair-guided LLM на каждый доступный source | exact snapshot/anchor/numeric binding; blank source → skipped; cancellation rethrow; result persistence idempotent по run/study/source/prompt | source длина, количество findings/selected Studies, repair; LlmAttemptCount в логах, не tokens |
| Evaluating | Current-run extraction/evidence → EvidenceEvaluation отдельно от результата | Один LLM на выбранный Study context; нет repair loop | grounded evidence projection/source signals, no raw unverified summary; no Evidence → skipped; persisted evaluation reuse | Evidence/context размер, количество Studies; categories/duration logs |
| Synthesizing | Bounded validated corpus/evaluations + persisted quantitative artifact → report/claims/citations | repair-guided LLM для непустого corpus; zero included Evidence → deterministic InsufficientEvidence без LLM | exact run lineage, structured claim selectors, deterministic numbers/text; existing report reuse | Studies/Evidence/claims bounds; report coverage/duration/attempts logs |

VERIFIED_IN_CODE E06–E13. Scientific failures оформляются через существующее failure handling, не как successful zero-result search. Host cancellation не считается медицинским провалом: worker пытается release, иначе lease истекает. Сохранённые результаты сокращают retries, но crash после внешнего ответа до commit допускает повторный внешний вызов. Отдельного total job-attempt budget нет.

## 4. Уже существующие SaaS-возможности

| Возможность | Статус | Доказательство / предел |
| --- | --- | --- |
| Authentication / sign-in | Implemented | VERIFIED_IN_CODE / VERIFIED_BY_CI E15/E16; реальный IdP UNKNOWN |
| User identity | Implemented | Immutable opaque JWT sub; не email/header; аккаунт живёт у IdP |
| Owner workspace | Implemented | Owner predicate в stores до paging/count; чужой/несуществующий run → 404 |
| Research history / multiple runs | Implemented | POST и owner-scoped list/detail; один пользователь может создавать многие runs |
| Async jobs / progress | Implemented | Durable ResearchRun, worker, status/progress HTTP и polling UI |
| Report viewing | Implemented | Report workspace, structured/legacy badges, citations/coverage |
| Evidence / source provenance | Implemented | Current-run Evidence и discovery paths, exact source metadata/anchors |
| Quantitative | Implemented | Persisted multiple groups, fixed/common + RE, Wald/HKSJ, heterogeneity, PI, forest plot/weights |
| Usage metering | PartiallyImplemented | Научные counts, attempts и durations; нет полного LLM usage accounting |
| Exports | PartiallyImplemented | window.print/print styles и внешние bibliographic links; не server PDF/download/public sharing |
| Recovery | Implemented | Lease/fence/heartbeat и реальные DB concurrency tests; не backup recovery |

VERIFIED_IN_CODE / VERIFIED_BY_CI E03–E18. History не требует новой organization entity. Schema ResearchQuestion → many ResearchRuns существует, но POST сейчас создаёт новую Question вместе с Run; отдельного rerun-existing-question HTTP use case не обнаружено.

## 5. Отсутствующие SaaS-возможности

| Возможность | Статус | Почему важно / не важно сейчас |
| --- | --- | --- |
| Per-user / global admission quotas | NotImplemented | NOT_IMPLEMENTED E03/E04: нельзя ограничить расходы одним пользователем |
| Token/cost ledger, billing reconciliation | NotImplemented | StructuredLlm metadata не возвращает usage; schema без ledger |
| Account deletion / run deletion | NotImplemented | Нет Delete endpoints/use cases, retention service |
| Retention automation | NotImplemented | Нет TTL/scheduled purge; срок фактически indefinite |
| Operator administration / failed-run replay / cancel UI | NotImplemented | Domain Cancelled не означает существующий HTTP/admin workflow |
| Billing / subscriptions / plans | NotRequiredForFirstDemo | Код не реализован; demand и economics не проверены |
| Organizations / collaboration / RBAC | NotRequiredForFirstDemo | Для индивидуального invite pilot текущий owner model достаточен |
| Markdown/PDF service export / public share link | NotRequiredForFirstDemo | Print уже есть; необходимость дополнительных formats UNKNOWN |
| Production deployment/secret platform | Unknown | Документация и preflight существуют, operator resources не предоставлены |
| Commercial demand / useful-report value | Unknown | Нет pilot feedback/оплаты/экспертных benchmark результатов |

NOT_IMPLEMENTED относится к проверенным маршрутам, DI, DbSets и frontend, а не к невозможности ручного действия администратором.

## 6. Production readiness

VERIFIED_IN_CODE: production JWT требует Authentication:Authority/Audience. DevelopmentLocal нельзя считать production auth. Отсутствие AI:ApiKey не мешает health/startup само по себе: provider/model/key проверяются при вызове. Это не отменяет обязательной production auth/DB конфигурации.

| Область | Доказано | Не доказано / отсутствует |
| --- | --- | --- |
| Process/DB health | /health/live без providers; /health/ready PostgreSQL; /health aggregate | Не проверяет scientific quality, worker backlog, migration version или recovery SLA |
| Fresh schema | MigrateAsync на empty Testcontainers DB; pending-model check | Upgrade/restore реальной production DB, RPO/RTO |
| API security | JWT validation, owner isolation, generic operational errors | Конкретный внешний IdP, внешний proxy/HTTPS, pen-test |
| BFF security | Route/method allowlist, Origin/CSRF checks, private no-store, bearer только server-side | Полный XSS protection, global IdP logout/revocation/rotation без interruption |
| Headers | nosniff, frame DENY, no-referrer, CSP base/object/frame restrictions | Это не полноценная script CSP; HSTS и TLS зависят от proxy |
| Worker | Durable leases, periodic heartbeat, fenced writes, cancellation | Dead-letter UI, total retry budget, load/capacity/fairness proof |
| Dependencies | Frozen lockfile/CI audit policy | Свежие advisories именно на 2026-10-10 без registry audit UNKNOWN |
| Deployment | Dockerfiles/preflight/runbook, CI synthetic fullstack | Автоматического production deploy job/фактического staging proof нет |

E04/E05/E16/E18/E19. Local Compose явно development: local auth, development password, exposed API/PostgreSQL ports, startup migrations. Это не готовый secure staging recipe.

Последний dependency audit артефакт 2026-10-09: production advisories всех severities 0; all-dependencies содержит dev HIGH braces <=3.0.3, GHSA-vfj7-8cjw-p6xm. В saved JSON указан patched_versions >=3.0.4; старое утверждение «нет patched версии» нельзя использовать как актуальный факт. Наличие опубликованного release и его пригодность сегодня UNKNOWN без отдельной проверки. Installed lockfile version 3.0.3. Prod HIGH/CRITICAL gate не подавлен. Policy tests не обновляют список advisories.

## 7. F19 Gate A / Gate B

**Gate A: VERIFIED_IN_CODE / VERIFIED_BY_TEST / VERIFIED_BY_CI.** Есть deployment validators, no-network default, bounded fake probes, documented prerequisites и synthetic auth/fullstack. В S0 deployment:test 40 passed, но это не реальный IdP.

**Gate B: NOT RUN / UNKNOWN runtime.** Реальные issuer/client registrations, два тестовых пользователя, exact scopes/audience/sub compatibility, HTTPS frontend/API, trusted certificate, proxy, production DB/operator settings отсутствуют в предоставленной конфигурации.

Проверялось только наличие переменных, не значения секретов: WEB_AUTH_ORIGIN, OIDC_ISSUER/CLIENT_ID/CLIENT_SECRET, WEB_SESSION_SECRET, Authentication__Authority/Audience, ConnectionStrings__MedResearch, AI__Provider/Model/ApiKey, OPENAI_API_KEY и live flags в процессе отсутствуют. Нет repo .env/.env.production и web .env.local/.env.production. Это не доказательство отсутствия ресурсов у оператора в другом месте.

DOCUMENTED_ONLY E19: требуется operator-supplied configuration и отдельное разрешение на external probes/state-changing POST. В S0 не создавались серверы, IdP accounts/clients, DNS, public endpoints, deployment resources; auth probes и test POST во внешний сервис не выполнялись.

## 8. Научная корректность: что проверено, а что нет

| Область | Узкая гарантия | Остаточная неопределённость |
| --- | --- | --- |
| Publication identity | Stable PMID/PMCID/DOI normalization, unique DB authority, race handling | No-ID publications не объединяются; duplicate trial/cohort не распознаётся автоматически |
| Provenance | Один Study, разные searches/discoveries, distinct per-run work | Два sources не означают полный systematic search |
| SourceMaterial | Immutable content/hash/version, exact material association; structured full text supported | Incomplete/licensing/source coverage; current shared snapshots не обязательно acquired в данном run |
| Numeric grounding | Local measure/effect/CI/statistical/role binding, Verified/Ambiguous/Unsupported | Не доказана семантика во всех biomedical grammar/table cases |
| Context compatibility | Explicit outcome/population/intervention/comparator/design/timepoint grouping | LLM interpretation этих labels не является expert-confirmed truth |
| Evaluation | Отдельные methodological domains; no raw-summary authority | Не formal RoB/GRADE, не клиническая certainty certification |
| Quantitative | Inverse variance, Q/df/I², REML, RE Wald, canonical HKSJ, PI: reference/negative tests | Научная пригодность input set и независимость cohorts не вычисляется из истины |
| Report claims | Closed structured claim semantics/selectors; server-owned numeric projection; legacy маркирован | Upstream direction/classification может быть ошибочной; causality не доказана |
| Citation lineage | Current run → claim/evidence → exact extraction/source → authoritative Study metadata | Правильная ссылка не гарантирует правильную интерпретацию |
| InsufficientEvidence | Детерминированный честный empty result, не operational failure | Полезность для клиента UNKNOWN |

VERIFIED_BY_TEST: ScientificTrustBoundaryCorrectionTests проверяет cross-bound/mislabelled statistics, hospital-vs-participant N и положительные role cases. StructuredNarrativeClaimTests отвергает unsupported authoritative prose. E12 quantitative BCG reference tests отдельно проверяют fixed/RE/REML/HKSJ/PI; HKSJ reference соответствует указанному в тесте metafor test="knha". Никакой live metafor/R rerun в S0 не делался.

VERIFIED_BY_CI E15: tests PersistSearchResultsAsync_SameStudyFromTwoSearchesInSameRunPreservesBothDiscoveryPaths, SameStudyFromPubMedAndEuropePmcPreservesSourceSpecificDiscoveryPaths, ConcurrentUpsertsForSameStableIdentityCreateOneCanonicalStudy и FindStudiesForExtractionAsync_MultipleSourceDiscoveriesProduceOneStudyWorkItemPerRun проверяют PostgreSQL invariants. Локально эти DB cases skipped.

Уточнение quantitative scope: распознавание/normalization различных effect families не означает pooling всех families. Текущий pooled path поддерживает OR/RR/HR; unsupported families должны оставаться explicit unavailable, а не «автоматически доступными».

VERIFIED_IN_CODE E07 и [Study.EnrichMissingMetadata](E:/MedResearch/src/MedResearch.Domain/Study.cs): scalar metadata дополняется только при existing null; null incoming не стирает существующее. Title/source сохраняют first-insert value; date parts дополняются только как совместимая группа. Authors/publication types объединяются case-insensitive, а не проходят field-authority arbitration. Это deterministic enrichment, не доказательство правильности cross-source author списка. Stable IDs, указывающие на разные persisted Studies, skip как hard identity conflict; same PMID с другой новой DOI не перезаписывает прежнюю DOI. No-ID records остаются отдельными; title-based merge нет.

### 8.1. Матрица научных рисков

| Риск | Статус | Основание |
| --- | --- | --- |
| Перепутанный OR/CI, sample role | PartiallyMitigated | Confirmed historical defect закрыт F15.1 negative tests; универсальная grammar accuracy Untested |
| Ошибочный outcome/population/timepoint | PartiallyMitigated | Exact anchors/group labels; экспертная валидность текущего real pipeline Untested |
| Неверный study design/methodology | Untested | Structured evaluation есть, calibration against experts нет |
| Retrieval omissions | PartiallyMitigated | Два bounded sources, coverage flags; recall benchmark нет |
| Одинаковое исследование в разных papers / overlap | TheoreticalRisk + NOT_IMPLEMENTED trial resolution | Unique Study означает publication, не trial |
| Incomplete abstract/full text | Confirmed limitation | SourceScope/truncation/availability; не всё полнотекстово |
| Необоснованная causal/certainty интерпретация | PartiallyMitigated | Structured limitations; downstream label truth не expert verified |
| Scientifically misleading but valid pooling | TheoreticalRisk | Числа и group compatibility могут пройти при ошибочно размеченных inputs |
| Недоступный source / partial provider success | Confirmed supported behavior | Durable attempts + continued search; coverage incomplete |

Нельзя объявлять эти потенциальные ошибки текущими confirmed production bugs без reproducer.

Принцип benchmark: единица публикации не всегда единица исследования; несколько publications могут относиться к одному trial. Для reference extraction и linkage нужен human review. [Cochrane Handbook, Chapter 5](https://www.cochrane.org/authors/handbooks-and-manuals/handbook/current/chapter-05). Ограниченный поиск в двух базах сам по себе не устанавливает полноту покрытия. [Chapter 4](https://www.cochrane.org/authors/handbooks-and-manuals/handbook/current/chapter-04).

## 9. Предложение scientific benchmark и безопасного live runbook

### 9.1. Двенадцать вопросов: только вопросы, не готовые ответы

Предлагаемые cases не являются рекомендациями лечения. Reference PMID/DOI, принятые conclusions, effect numbers и gold labels **ещё не собраны**. Их должен утвердить предметный reviewer до оценки модели.

| ID | Вопрос для benchmark | Категория / обязательный фокус reference |
| --- | --- | --- |
| B01 | Улучшает ли creatine по сравнению с placebo working memory у здоровых взрослых? | Intervention; age, dose, тест cognition, timepoint; RCT extraction |
| B02 | Как CPAP по сравнению с sham/usual care влияет на daytime sleepiness у взрослых с obstructive sleep apnea? | Intervention; instrument/scale, follow-up, comparator |
| B03 | Связана ли длительность сна с incident type 2 diabetes у взрослых без diabetes на baseline? | Observational; adjustment, baseline status, causality ограничена |
| B04 | Как intake кофе связан с all-cause mortality в prospective adult cohorts? | Conflicting/observational; exposure groups, adjusted estimates, overlap cohorts |
| B05 | Как probiotics по сравнению с placebo влияют на antibiotic-associated diarrhea у взрослых? | Heterogeneous formulations, matched outcome/timepoint |
| B06 | Отличаются ли effects aerobic exercise на depressive symptoms у подростков и у взрослых? | Population mismatch; отдельные группы, не общий pooled answer |
| B07 | Отличаются ли effects cognitive behavioral therapy на chronic insomnia через 4–8 недель и через 6–12 месяцев? | Timepoint mismatch; validated scales, repeated outcomes |
| B08 | Какие quantitative и qualitative результаты доступны для home telerehabilitation после stroke? | Mixed design; outcome separation, qualitative-only branch |
| B09 | Как пациенты описывают барьеры adherence к inhaled therapy при asthma? | Qualitative-only; не изобретать OR/CI |
| B10 | Как digital reminders влияют на medication adherence при редких inherited metabolic disorders? | Limited evidence; допустим InsufficientEvidence, не обещать zero studies |
| B11 | Как intranasal corticosteroids по сравнению с placebo влияют на nasal symptoms при allergic rhinitis? | Potential pooling; measure/scale/design/timepoint совместимость |
| B12 | Каковы effects intervention в cluster-randomized school programs по prevention smoking у подростков? | Cluster N vs participant N; adjusted variance, secondary publications |

Для каждого case: два заранее согласованных human search strategies; actual search dates/queries; source IDs/licences/snapshots; eligibility rules; независимо проверенные study design/population/comparator/timepoint, sample roles, exact estimate/CI/SE/p/confidence tuple и exact source anchors. Disagreement решает эксперт; неопределённое поле остаётся неизвестным. AI draft не gold standard.

Публикации одного trial/cohort связываются reviewer вручную. Reference set split development/holdout; negative fixtures включают два похожих estimates, wrong population, incompatible timepoints, missing confidence level, absent full text и paper/cohort duplicates. Freeze reference version, model/prompt/config/code HEAD и source snapshots; изменившийся reference не сравнивать без version.

### 9.2. Метрики и знаменатели

| Метрика | Как считать |
| --- | --- |
| Search relevance | Human-eligible retrieved unique publications / reviewed retrieved publications |
| Reference recall | Найденные eligible reference units / все eligible units в утверждённом reference set; не global recall |
| Source availability | Retrieved publications с пригодным source / retrieved publications; отдельно abstract/full text/licensed |
| Extraction precision / recall | Correct matched human findings / emitted findings; correct matched findings / reference findings |
| Outcome/context accuracy | Верные outcome/population/intervention/comparator/timepoint tuples / проверенные tuples; отдельно каждое поле |
| Numeric tuple accuracy | Полностью верные measure+effect+CI/SE+p+N-role+confidence/context / проверенные tuples |
| Citation correctness | Claims с правильным current-run Evidence/source/article linkage / проверенные claims |
| Unsupported claim rate | Не поддержанные reference claims / все проверенные claims |
| False-positive Verified | Система Verified, reviewer unsupported/ambiguous / все system Verified; отдельно grammar category |
| Useful-report rate | Expert-rated полезные для заявленного writing task reports / все accepted runs |
| InsufficientEvidence | Completed InsufficientEvidence / accepted runs; reviewer отдельно «честный» vs retrieval/extraction miss |
| Completion / operational failure | Completed / accepted; Failed / accepted; cancellation отдельно |
| Human correction time | Минуты correction на report; median/range и конкретные reviewers |
| Coverage loss | Cases, где cap/provider/source failure исключил reference data / reviewed cases |

Никаких baseline percentages сейчас нет: UNKNOWN. До pilot критерий контроля: каждый false-positive Verified/unsupported authoritative claim разбирается; не утверждать 0-error rate по маленькой выборке. Acceptable thresholds usefulness/recall/expert time задаются совместно с reviewer, не выдаются за измеренные.

### 9.3. Найденный дефект live harness

**VERIFIED_IN_CODE E14; P0 prerequisite перед S1:** ResearchPipeline_WithRealProviders_CompletesBoundedLiveRun читает MEDRESEARCH_LIVE_E2E_CONNECTION_STRING, MEDRESEARCH_LLM_PROVIDER, OPENAI_API_KEY/OPENAI_MODEL/PUBMED_EMAIL для Skip/gates, но LiveFactory.ConfigureWebHost только выставляет Development. Checked aliases не передаются в host configuration.

**INFERRED failure scenario:** MEDRESEARCH_LIVE_E2E_CONNECTION_STRING может пройти gate, а приложение использовать development connection вместо изолированной DB. Выбор Codex через MEDRESEARCH_LLM_PROVIDER не меняет AI:Provider; aliases ключа/model/email также не эквивалентны effective .NET config. Нельзя считать такое выполнение isolated или bounded. Это не доказанное изменение production DB: сценарий в S0 не запускался.

Кроме того, actual factory не переопределяет MaxSearchQueries/selected Studies, не гарантирует migration/cleanup. Assertions Completed, report 200, discovered >0 и nonblank conclusion допускают InsufficientEvidence. Проверка searchQueryCount <=4 после выполнения не является pre-call cost guard.

DOCUMENTED_ONLY discrepancy: ADR-015/старые README описывают bounded overrides; фактический test их не содержит. F12 описывает исторический live completion, но не доказывает post-F16 scientific accuracy или безопасность текущего harness.

### 9.4. Runbook для отдельного будущего разрешения, НЕ команды к запуску сейчас

1. Сначала SCI-002: доказать fake-host tests, что gate configuration == effective configuration; проверить отдельную DB identity, provider/model, exact caps и migration setup. До этого существующий harness не запускать по одному наличию aliases.
2. Получить operator approval на конкретные literature/full-text/LLM calls, budget, benchmark case, database и cleanup. Codex subscription нельзя трактовать как бесплатный production cost.
3. Подготовить isolated disposable PostgreSQL и положительно проверить server/database identity. Запретить production endpoints/DB; нормальные ConnectionStrings__MedResearch и AI__Provider/Model должны соответствовать проверенным values. Не печатать credentials.
4. Согласовать tiny bounds: например один query, два sources, <=3 results/source, <=2 source acquisitions/extractions/evaluations, repair <=1; это будущий test config, не уже реализованные defaults.
5. Выбрать production OpenAI отдельно от development CodexCli. Codex executable/login и MEDRESEARCH_RUN_LIVE_CODEX_CLI требуются только для development opt-in. API path требует operator key/model. PubMed contact email, optional key; Europe PMC без invented key. Gate flags вручную после approval.
6. Проверить worker/migration/health и остановку; иметь operator kill switch и учёт уже оплаченных in-flight calls. 15-minute polling timeout теста не является общей остановкой расходов.
7. Выполнить один case; собрать accepted/failed/insufficient status, exact plan/search/source/extraction/evaluation/report/artifact lineage, model/prompt/config versions, every external attempt, actual usage (если доступна), timings и review labels.
8. Эксперт проверяет report usefulness и every claimed fact. Не заменять эту проверку pipeline Completed.
9. Остановить worker/host; удалить disposable DB и временные artifacts согласно approval; для сохранённых benchmark snapshots проверить licence/retention. Записать cleanup outcome, не предполагать automatic deletion.

S0 не включал flags и не делал эти calls. Standalone PubMed/Europe PMC live smoke и live E2E projects остаются opt-in, не нормальным CI.

## 10. Полный inventory LLM-вызовов

VERIFIED_IN_CODE E06/E09/E10/E11/E13: четыре научных caller и один общий repair service. У ScientificLiteratureSearchCoordinator, SourceMaterialAcquirer, quantitative calculators и HTTP report projection LLM-вызовов нет.

| Caller / метод | Prompt / вход | Calls и retry | Usage / persistence |
| --- | --- | --- | --- |
| ResearchPlanner.GenerateAndPersistPlanAsync | ResearchPlannerPrompt, research-planner-v1; question + bounded query schema | Один direct GenerateStructuredAsync до accepted plan; без repair/transport retry | Provider/model/time/prompt в plan; tokens/cost отсутствуют |
| EvidenceExtractor.ExtractAsync | EvidenceExtractionPrompt, evidence-extractor-v3-bound-tuples; exact SourceContent + question/plan/Study | GenerateAndValidateAsync; 1 + RepairBudget на выбранный source/Study; blank source 0 | Accepted extraction/evidence provenance; AttemptCount в logs, не usage ledger |
| EvidenceEvaluator.EvaluateAsync | EvidenceEvaluationPrompt, evidence-evaluator-v2-no-raw-summary; grounded evidence/supporting text + source signals | Один direct GenerateStructuredAsync на Study context; no evidence 0; repair нет | Evaluation provenance/categories; tokens/cost отсутствуют |
| ResearchSynthesizer.SynthesizeAsync | ResearchSynthesisPrompt, research-synthesizer-v3-structured-claims; bounded validated corpus + deterministic quantitative results | GenerateAndValidateAsync; 1 + RepairBudget на report; empty validated corpus 0 | Report/claims/quantitative lineage, attempt logs; cost отсутствует |
| ValidationGuidedLlmRepairService.GenerateAndValidateAsync | Тот же schema/source, validator feedback appended при repair | Физический GenerateStructuredAsync внутри bounded loop; budget default 1, допустимо 0..2 | ValidatedStructuredGeneration содержит временный AttemptCount/RepairedIssueCodes |

Versions проверяются в prompt code; scientific output валидируется независимо от JSON schema. Repair не ослабляет валидаторы, не подставляет числа и не распространяется на все stages.

OpenAIStructuredLlmClient: IHttpClientFactory, POST responses, strict structured JSON, AI:Provider=OpenAI, configurable AI:Model/ApiKey/BaseUrl. Model/key не настроены для этого аудита. TimeoutSeconds default 30 (bounded 1..300); MaxOutputTokens default 2000 (bounded 256..8000). Input token budget, OpenAI transport retries и durable usage accounting отсутствуют. Provider response usage не включается в StructuredLlmProviderMetadata.

StructuredLlmRequest = prompt version/system/user/schema; metadata = provider/model/responseId/generatedAt. ResponseId в контракте не означает, что каждый external attempt сохранён в DB. Ни tokens, ни money contract не возвращает.

CodexCliStructuredLlmClient: development/manual only, executable codex, read-only sandbox, unique temp workspace, schema/last-message output; MaxPromptCharacters default 500000 (range 1000..2000000), TimeoutSeconds 300 (range 10..1800). Это character bound, не token/price measurement. Production OpenAI economics не выводятся из development allowance.

**VERIFIED_IN_CODE, P1 cost/safety gap:** OpenAI adapter читает весь body через ResponseContentRead/ReadAsStringAsync; отдельного response-byte cap/body deadline как у hardened scientific providers нет. MaxOutputTokens не ограничивает arbitrary HTTP error response body. Не доказана эксплуатация; это bounded-resource hardening candidate.

## 11. Unit economics: прозрачная модель

Ни production model, ни rate card, ни measured tokens/стоимость в репозитории не установлены. Денежные значения **UNKNOWN**, не ноль.

Для каждого внешнего LLM attempt a:

    C_llm(a) =
      InputTokens(a) * PriceInput(provider, model, pricingVersion) / 1_000_000
      + OutputTokens(a) * PriceOutput(provider, model, pricingVersion) / 1_000_000
      + только подтверждённые прочие provider charges

Не применять одну цену к неизвестным cached/reasoning/billing categories. Unknown usage после timeout учитывается как unknown потенциальный расход, а не «не было вызова».

    MarginalCostPerRun =
      PlanningLLMCost + ExtractionLLMCost + EvaluationLLMCost
      + SynthesisLLMCost + RepairLLMCost
      + ProviderCosts + IncrementalCompute + IncrementalStorage + IncrementalOperations

Fixed monthly отдельно: API/Web/worker host, PostgreSQL, reverse proxy/TLS, IdP, backup, monitoring и базовые logs/artifact storage. Их распределение на report зависит от реального объёма; при нулевом числе useful reports cost/useful report не определён.

    CostPerUsefulReport =
      Costs всех accepted runs, включая Failed/InsufficientEvidence/retries
      / число expert-rated useful reports

Количество базовых generation за один проход:

    N_base = P + E + V + S
    N_with_repairs <= P + E * (1 + r) + V + S * (1 + r)

P обычно 1, E = реально выбранные source-bearing Studies, V = реально выбранные Studies с Evidence, S = 0 или 1, r = RepairBudget. Это не lifetime cap при recovery.

### 11.1. Сценарии: расчётные предположения, НЕ измерения

| Сценарий | Предполагаемая конфигурация/нагрузка | LLM calls за один проход | Literature/full-text HTTP |
| --- | --- | --- | --- |
| LIGHT | 1 query; 2 sources; <=3 results/source; E=2,V=2,S=1; r=0 | Base/max 6 | При nonzero PubMed batch + одной Europe page: 3 search/fetch + <=2 full-text = <=5 base |
| STANDARD | 2 queries; 2 sources; <=10 results/source; E=6,V=6,S=1; r=1 | Base 14; с full repair <=21 | 6 search/fetch + <=6 full-text = <=12 base |
| HEAVY | Defaults: 5 queries, 2 sources, 10 results/source; E=10,V=10,S=1; r=1 | Base 22; с full repair <=33; при r=2 <=44 | 15 search/fetch + <=10 full-text = <=25 base |

HTTP counts предполагают successful bounded first traversal, nonempty search, configured batch25/page25 и достаточное наличие PMCID для upper bound full-text. Zero results убирают EFetch; pagination, source settings и failures меняют числа. Default HTTP retry2 означает до 3 attempts на logical request: пример HEAVY до 75 attempts, не 75 LLM generations. При допустимом retry5 аналогичный условный предел 150. Recovery/повторная source acquisition сюда не включены.

Tokens на каждый stage UNKNOWN. Длина входа зависит от источника, schema, числа grounded findings и repair feedback; chars нельзя переводить в tokens константой без измерения. Output cap задаёт allowance одного запроса, не фактически оплаченные tokens.

Compute drivers: сериализация/parse, source normalization, corpus construction, DB read/write, ожидание внешнего provider. Extraction store загружает все discovered Studies/current source bodies до окончательного отбора maxStudies: это не SQL memory cap. Storage drivers: source versions/content, Evidence/anchors, attempts, artifacts/contributions, отчёты, backup и logs. Latency/RAM/CPU/per-GB cost не измерены.

Стоимость PubMed/Europe PMC запросов не задана приложением. Не включать выдуманную per-request цену; даже при отсутствии provider invoice есть время, compute, policy и reliability cost. Денежные договорные условия отдельно проверяет оператор.

## 12. Retry amplification и recovery

| Механизм | Actual behavior | Экономическое следствие |
| --- | --- | --- |
| Scientific HTTP retry | 429/selected 5xx/transport/timeout, bounded backoff, Retry-After/cancellation | Повторный HTTP attempt расходует quota/latency, не LLM tokens |
| LLM validation repair | Только extraction/synthesis, default +1, max +2; non-repairable/provider errors не «чинятся» | Дополнительная generation с похожим/увеличенным input |
| Accepted stage result reuse | Plan/search/extraction/evaluation/report idempotency keys | Снижает повторы уже committed результата |
| Lease recovery | Restart current stage после expire; stale scientific writes fenced | Оплаченный ответ до commit может быть утрачен и запрошен заново |
| Source acquisition reentry | Content hash reuse при persist; provider может вызываться заново | DB dedup не равен HTTP cache/no-call |
| POST retry/manual resubmit | SDK mutation retry не делает create автоматически повторно; durable idempotency key отсутствует | Повтор после ambiguous response способен создать новый billed run |

VERIFIED_IN_CODE E05–E13. Old-owner fence protects science, но не возвращает деньги за отправленный запрос. New owner может вызвать тот же provider после crash; total recovery attempt budget не найден.

**Найденный подтверждённый gap:** MaxStudiesPerRun в extraction/evaluation фактически ограничивает **очередную партию необработанных records**, не cumulative count за жизнь Run. E09.FindStudiesForExtractionAsync исключает existing extraction для selected source и затем набирает maxStudies. E10.FindStudiesForEvaluationAsync сначала исключает existing evaluations и затем Take(maxStudies).

**INFERRED scenario:** первый worker успевает часть initial batch, падает; новый выбирает оставшиеся + дополнительные Studies. Accepted distinct count может превысить advertised 10. Новый selected current SourceMaterial также может потребовать re-extraction прежнего Study. Новый PostgreSQL reproducer в S0 не добавлялся.

Следовательно, 22/33/44 выше не являются строгими per-run financial ceilings. При произвольном количестве crashes строгого конечного enforcement ceiling оплаченных calls нет. Решение будущего SAAS-002: durable run work set/remaining budget + attempt metering/pre-call guard, сохраняя honest missing/unavailable states.

## 13. Существующие usage-метрики

VERIFIED_IN_CODE E04–E13:

- ResearchRun CreatedAt/StartedAt/CompletedAt, status/failure; lease owner/version/expiry/heartbeat.
- ResearchPlan provider/model/prompt/generated timestamp.
- LiteratureSearch source/query/execution/returned count; logical LiteratureProviderAttempt statuses, duration/failure classification.
- ResearchStudyDiscovery paths и distinct counts.
- SourceMaterial type/provider/hash/version/content length/truncation/current flag; available full text vs abstract.
- Extraction/evaluation persisted outcomes/categories; stage selected/skipped/findings/durations.
- Synthesis coverage: discovered/extracted/evaluated/included Studies, findings/claims, insufficiency/limitations.
- Structured logs: stage and provider events; LlmAttemptCount/RepairedIssueCodes у extraction/synthesis.
- Persisted quantitative artifact/fingerprint/contribution set для воспроизводимости, не billing.

LiteratureProviderAttempt = один logical source/query, не каждый HTTP retry. Started после crash может остаться незавершённым: отсутствие завершения не доказывает no external call. Scientific rows count != usage accounting: skipped/no-source units могут иметь zero LLM calls, repair/crash calls могут не создать accepted rows.

## 14. Недостающие usage-метрики

NOT_IMPLEMENTED E13/E04: нет complete attempts ledger, provider token usage, pricing version/currency, actual monetary cost, per-owner/global counters, durable repair count aggregation и reconciliation неизвестных billed attempts.

Минимальный будущий набор измерений: run/stage/caller/prompt/model/provider, attempt ID + provider response ID, started/finished/outcome/cancel/timeout, input chars отдельно от measured tokens, reported token categories, repair/recovery reason, elapsed time, cost source/rate-card version, unknown-usage marker.

Для source HTTP: logical search attempt + request/retry/page/batch count/bytes/duration; для full text: unavailable vs provider failure/fallback/reuse. Для продукта: useful/insufficient/failed rate, reviewer time, queue age, per-owner admissions и reserved/remaining limits.

**INFERRED design rule:** запись факта внешнего расхода не должна исчезать только потому, что worker потерял scientific lease. Scientific stale writes должны быть запрещены; accounting receipt должен позволять зафиксировать реально совершённый old-owner attempt отдельно и безопасно. Не смешивать эти два разных authority.

Не нужен Stripe или полноценная аналитическая платформа для первых измерений. Structured durable accounting + simple operator report достаточны; где usage отсутствует, честно unknown.

## 15. Текущие quotas и rate-limit boundaries

| Ограничение | Actual default / range | Что НЕ гарантирует |
| --- | --- | --- |
| Planning query count | ResearchPlanning:MaxSearchQueries=5, hard5; query length300 | Число исследований пользователя/сутки |
| PubMed | results10 hard200; batch25 hard200; rate2, validation <=3 без key/<=10 с key | Общую quota других приложений/IP/replicas |
| Europe PMC | results10 hard200; page25 max100; rate2, app max5 | Значение5 не объявляется official Europe PMC limit |
| Source acquisition | MaxStudiesPerRun10 range1..50; MaxContentCharacters30000 range1..200000 | Общий размер всех sources/LLM token budget |
| Extraction/evaluation | MaxStudiesPerRun10, bounded1..50 | Durable cumulative limit через recovery |
| Synthesis | Studies10 cap50; Evidence40 cap100; Claims12 cap25 | Per-owner budget/month или clinical utility |
| LLM output | AI:MaxOutputTokens2000, bounded256..8000 | Input tokens, total repairs/recovery cost, HTTP bytes |
| BFF transport | POST body16384 bytes, response cap10000000 bytes | API direct question length/account volume protection |
| Single hosted worker | Один sequential processor на API instance | Global distributed cost/concurrency cap |

VERIFIED_IN_CODE E04/E09–E13/E16. Provider limiters central внутри конкретного процесса; распределённый общий egress rate across replicas не доказан.

**VERIFIED_IN_CODE gap:** candidate abstract сохраняется целиком в EfScientificSearchResultStore.AddAbstractSourceMaterialIfPresentAsync, также abstract path SourceMaterialAcquirer; выбранный SourceContent передаётся extractor. Full-text 30000 не является universal abstract/prompt cap. Scientific provider response-byte caps уменьшают риск, но это другой budget.

**VERIFIED_IN_CODE gap:** ResearchQuestion требует nonblank, DB ограничивает text до1000; API CreateResearchRequest/use case не ограничивает length, frontend только min12, без согласованного maxlength. **INFERRED:** >1000 может дойти до DB и стать generic500 вместо validation400. Reproducer не запускался; BFF16KB ограничение не устраняет direct API scenario.

Ни provider rate limiter, ни scientific cardinality cap не препятствуют пользователю поставить тысячи Queued runs. Count/daily/spend/user/global quota NOT_IMPLEMENTED.

## 16. Рекомендуемые минимальные quotas

Предложение, не существующая capability и не измеренная capacity: для первого invite pilot owner outstanding1, global outstanding2, owner daily2, global daily10, bounded queue и operator stop-new-admission. Числа согласуются после измерений; их нельзя продавать как SLA.

Outstanding = Queued + active processing, а не только уже claimed. При ограничении только active пользователь может наполнить очередь и зарезервировать будущие расходы.

Admission должна в одной короткой PostgreSQL transaction: проверить global stop/лимиты, owner, owner-scoped idempotency key/body hash; зарезервировать daily admission; создать Question/Run. При rejection не оставлять partial Question/Run и не инициировать LLM. Порядок locks global → owner; не SELECT COUNT затем INSERT без сериализации.

Простой first-demo вариант: fixed advisory transaction locks + расчёт outstanding по существующим ResearchRuns; durable daily reservation/idempotency rows с unique constraints и UTC window. Можно guard-row conditional updates; выбрать один локальный pattern, не создавать quota framework/Redis.

Failures/InsufficientEvidence потребляют daily admission: внешний расход мог состояться. Terminal status освобождает outstanding slot посредством actual state, не double-release counter. Recovery сохраняет тот же run/reservation. Failed/cancelled завершение нельзя превратить в loophole бесконечной бесплатной попытки.

HTTP status/error contract должен различать validation, owner quota, global capacity и temporary stop; rejected POST не считать scientific failure. Проверка оба пользователя/parallel processes обязательна в PostgreSQL CI.

Admission quotas ограничивают объём, **не дают точной денежной гарантии**. Для hard spend budget нужны SAAS-002 metering/pre-call reservation и lifetime bounds. До этого pilot только под operator budget supervision, не unlimited public launch.

## 17. Варианты cost-control архитектуры

| Вариант | Плюсы | Ограничение | Решение |
| --- | --- | --- | --- |
| Только существующие scientific caps | Уже реализовано | Нет пользователя/global budget; recovery gap | KEEP для science, не SaaS accounting |
| Только manual invite + monitoring | Минимум кода | Участник/ошибка клиента может истратить budget до реакции | Дополнительная защита, недостаточно отдельно |
| PostgreSQL atomic admission + simple usage accounting | Reuse transactions/owner/run; без новой инфраструктуры | Нужны concurrency tests и unknown-cost handling | Предпочтительный small change |
| Distributed quota/rate platform/Redis | Может понадобиться при многих replicas | Сейчас лишнее и не решает scientific quality | DEFER |
| Billing entitlements/Stripe | Позже monetization | Нет demand и unit economics | DEFER |
| Альтернативный LLM/model/agent swarm | Потенциально другое cost/quality | Без benchmark сравнение спекулятивно | REPLACE_ONLY_IF_PROVEN |

INFERRED: уменьшение input sources/repair budget может снизить cost, но ухудшить recall/usefulness; сравнивать по useful-report cost, а не только меньшему token count. Не отключать validation ради экономии.

## 18. Владение данными и изоляция

VERIFIED_IN_CODE E03/E04/E15/E16: immutable ResearchQuestion.OwnerSubjectId (bounded200) от authenticated sub; run принадлежит Question. В действующей one-issuer конфигурации opaque sub достаточен для individual SaaS. При будущем multiple issuer нужны namespace/issuer-aware identity, не универсальная уникальность всех sub в мире.

VERIFIED_BY_CI: JwtApiTests.SignedJwtUsers_AreIsolatedByActualPostgreSqlStores проверяет реальные stores; RealJwtHandler_RejectsInvalidIdentity и UsesSubNotEmailOrHeaders проверяют signature/issuer/audience/expiry/policy. Owner filter применяется до count/paging. Chosen wrong owner не может GET чужой report/evidence/provenance/quantitative через tested paths.

Global Study/SourceMaterial намеренно shared: publication IDs/metadata/content snapshots, не Question/Run-specific Evidence/report. Discovery допускает несколько searches одного Study; extraction/evaluation/report остаются current-run. Нет direct глобального Studies/source-content listing API.

**INFERRED residual:** source metadata для discovered Study может включать global snapshots других runs; это reuse scientific publications, не доказанная утечка personal Question. Нельзя обещать, что каждый shared snapshot acquired конкретно этим пользователем. Запрет private/patient/user uploads в shared source model нужен до такой новой функции.

Encrypted server-readable session cookie не account database. Logout очищает cookie/cache и broadcasts tab events; dormant tab не обязательно мгновенно удалит уже нарисованный текст. API продолжает enforcement. Revocation пользователя у IdP не равна немедленному отзыву всех уже выданных stateless sessions/access tokens.

Team/RBAC/organization не обязательны first demo; отсутствие их не owner-isolation bug.

## 19. Retention и deletion

| Данные | Текущая политика | Минимальный будущий подход |
| --- | --- | --- |
| Questions/runs/plans/evidence/evaluations/reports/claims/attempts/artifacts | Indefinite DB retention; user deletion NOT_IMPLEMENTED | Выбрать срок pilot, owner-scoped terminal-run deletion graph |
| Shared Study/source snapshots | Global durable scientific identity/content | Не удалять при одном user delete, пока есть references; licence policy отдельно |
| Logs/CI artifacts | Нет общей audited retention policy | Ограничить срок и доступ; не логировать sensitive question payload |
| Auth session | Cookie max age, access-token-bound expiry; default session3600sec | Не путать logout с historical data deletion |
| Backups | Operational policy/restore UNKNOWN | Retention/deletion propagation в backup runbook |

VERIFIED_IN_CODE E04/E16: relationships не все cascade. Restrict FKs coexist с cascade claims/claim-evidence/search discoveries/artifact contributions. Простая Delete(ResearchRun) не является безопасным graph deletion.

Будущий minimum: запрет/остановка active-run deletion или безопасный lease-aware lifecycle; owner check; короткая transaction удаления user-owned descendants в FK order; preserve global records, используемые другими runs; отдельная garbage-collection policy для unreferenced licensed snapshots; доказать two-user sharedStudy test. Не изменять existing immutable snapshot, чтобы «удалить текст из отчёта».

Экспорт/delete credentials/identifiers IdP и retained user research - разные процессы. Account deletion workflow пока отсутствует.

## 20. GDPR, copyright и legal review

Это review flags, **не legal compliance заключение**. Jurisdiction, lawful basis, operator roles, commercial licence и provider data processing договоры UNKNOWN.

Если GDPR применим: определить controller/processor, минимизацию, срок хранения/erasure, меры безопасности, special-category health data и transfers. Для раннего pilot запретить patient-identifying questions, names, records/uploads; объяснить передачу scientific query/контекста внешним processors и ограничить logs. [Официальный GDPR, Articles 5, 9, 17, 28, 32, 44](https://eur-lex.europa.eu/eli/reg/2016/679/oj/eng).

VERIFIED_IN_CODE: SourceMaterial.License nullable; наличие API/full text/PMCID не доказывает право на commercial storage/redistribution. Для каждого сохраняемого full-text проверить конкретную licence, allowed access channel, attribution и export rights. [PMC copyright](https://pmc.ncbi.nlm.nih.gov/about/copyright/), [PMC Open Access Subset](https://pmc.ncbi.nlm.nih.gov/tools/openftlist/). Не объявлять весь PMC коммерчески свободным и не путать текущий Europe PMC API с разрешением произвольного PMC crawling.

Scientific queries способны содержать sensitive user input. Часть search-reuse structured logs включает query: «не логируем secrets» не означает отсутствие personal data. Future policy требует redaction/minimization и operator access review.

External LLM retention/training/region условия и Codex account terms отдельно подтверждаются выбранным deployment/provider договором. Development account allowance не commercial processing agreement.

## 21. Operations: backup, restore, monitoring

| Функция | Факт / класс |
| --- | --- |
| Fresh DB migration | VERIFIED_BY_CI: Testcontainers MigrateAsync; модель актуальна локально |
| Production migration procedure | DOCUMENTED_ONLY E19: apply once before traffic, startup auto-migration не staging default |
| Worker restart/reclaim | VERIFIED_IN_CODE / VERIFIED_BY_CI E05/E15 |
| Backup scripts / pg_dump + restore rehearsal | NOT_IMPLEMENTED в проверенных repo scripts/runbooks; реальная operator практика UNKNOWN |
| RPO/RTO / offsite encrypted backup | UNKNOWN |
| Schema rollback | Не проверен; fresh migration success не rollback guarantee |
| Monitoring/alerts/dashboard | ILogger/health есть; OpenTelemetry/alert platform NOT_IMPLEMENTED/UNKNOWN external |
| Dead-letter/operator retry UI | NOT_IMPLEMENTED |
| Secret/session-key rotation | Частично DOCUMENTED_ONLY; multi-key continuity/forced logout procedure не runtime verified |
| Kill switch | ResearchProcessing:Enabled отключает worker при конфигурации/start; не гарантирует мгновенную отмену already billed calls |

Минимум OPS-001: agreed RPO/RTO; documented encrypted backup schedule/access/retention; восстановить backup в isolated DB и прочитать owner-scoped known report graph; versioned migrations и rollback/roll-forward decision; alert DB unavailable, worker no heartbeat, queue age, repeated failures, usage/budget; incident IDs и owner-safe diagnostics.

INFERRED fairness risk: queue SQL предпочитает Queued перед expired active; при постоянно прибывающих queued jobs recovery может ждать дольше. Это не доказанный production incident; нагрузочный fairness test отсутствует. Для закрытого ограниченного pilot сначала caps/queue-age alarm, не scheduler rewrite.

API error mapping отделяет DB operational503/500 от scientific zero results; failure text в HTTP sanitised. Содержимое инфраструктурных logs и доступ к ним отдельно должны пройти privacy review.

## 22. Frontend: текущий путь пользователя

VERIFIED_IN_CODE E16/E17; VERIFIED_BY_CI synthetic paths. В S0 прочитан также saved F19 mobile report screenshot: это fake persisted report, не настоящий медицинский результат; нового browser session не было.

| Шаг | Реальная возможность | Остаточный вопрос для pilot |
| --- | --- | --- |
| Login | OIDC redirect/callback/logout/session; tokens не в browser localStorage | Real issuer/config ещё Gate B |
| Dashboard/history | Owner history, status, navigation | First-user comprehension не измерена |
| New research | Form, mutation, created run routing | Нет quota feedback; question length gap |
| Run details/progress | Actual status/stage polling, failure/terminal panels | Не ETA/SLA; Failed не customer-useful outcome |
| Evidence/provenance | Study→search/discovery/source/extraction/evaluation inspection | Term Verified нужно объяснить как deterministic support, не clinical proof |
| Quantitative | Persisted groups, Wald/HKSJ/PI, explicit unavailable, forest, contribution lineage | Medical writer может не знать различие CI/PI/heterogeneity |
| Report | Coverage/sources/limitations, structured vs legacy, expandable citations | Completed/InsufficientEvidence и useful result различать в pilot review |
| Print/source links | Print report + PMID/PMCID/DOI external links | Additional export need UNKNOWN |
| Studies/settings | Placeholder/operator-oriented pages, не полноценный library/account control | Не считать готовыми SaaS administration/settings |

Минимальные UX изменения будущего: honest quota/capacity messages, aligned max question length, plain-language «что проверено/не проверено», clearly distinguish source failure vs no validated evidence. Это предложения, не подтверждённые проблемы всех пользователей.

Не объявлять отсутствующими уже существующие evidence panels, quantitative UI, report print и login. Public share links не нужны, пока ownership/licence/privacy договорённости отсутствуют.

## 23. Минимальная invite-only демонстрация

INFERRED recommendation: web-only, ограниченный набор invited individual users из внешнего IdP, одна Question → один Run, bounded source/LLM work, history/report/traceability/print. Manual приглашения и operator support допустимы; enterprise accounts/billing не нужны.

Перед доступом: Gate B, atomic admissions и controlled budget, approved tiny benchmark с reviewed outputs, operational backup/retention/notice. Демонстрация openly experimental и не medical advice; live missing/insufficient/error states не маскируются красивым report.

Success pilot измеряет usefulness и correction time, не число регистраций и не количество Completed. Честный insufficient result может быть научно правильным, но не всегда оплачиваемой ценностью. Отзывы 10–50 invite users не заменяют load measurement.

## 24. Launch blockers

P0 = блокирует безопасный самостоятельный controlled live launch; не требование перед показом fake local UI.

| ID / приоритет | Проверенный gap | Explicit failure scenario | Минимальное снятие |
| --- | --- | --- | --- |
| B1 / P0 | NOT_IMPLEMENTED owner/global quotas, NOT_IMPLEMENTED lifetime spend guard | Один authenticated user создаёт множество accepted jobs; recovery увеличивает calls; budget exhausted | Atomic admission + operator stop, затем measured attempt/lifetime budget |
| B2 / P0 перед S1 | VERIFIED_IN_CODE live gate/config mismatch E14 | Gate принимает isolated alias, host берёт другой connection/provider; test запускает не тот environment | SCI-002 effective config/DB identity/bounds tests до opt-in |
| B3 / P0 для live service claims | UNKNOWN current scientific usefulness/accuracy | Numerically valid report misleading; technically Completed пустой output рекламируется как evidence answer | SCI-001 expert benchmark, honest pilot limits |
| B4 / P0 перед external users | UNKNOWN real IdP/HTTPS, Gate B NOT RUN | Synthetic-compatible auth/ proxy configuration несовместима с real issuer или HTTPS cookies | DEPLOY-001 с operator environment/approval |
| B5 / P0 перед durable user data | NOT_IMPLEMENTED/UNKNOWN backup/restore/retention | DB loss без проверенного restore; sensitive questions хранятся бессрочно без policy | OPS-001 restore rehearsal/minimum data policy |

Отсутствие scientific validation измерений подтверждено; сами ошибки на живых medical questions не объявляются observed. Отсутствие real deployment evidence не означает доказанную vulnerability.

## 25. Engineering priorities

| Приоритет | Работа / user value | Evidence / риск | Simplest implementation / modules | Migration / external / size / verification |
| --- | --- | --- | --- | --- |
| P0 | SAAS-003: ограничить accepted volume | B1, E03/E04; unlimited POST | Existing owner/run admission transaction + locks/reservations/idempotency | Вероятно новая маленькая migration; no external; M; PG contention tests |
| P0 prerequisite | SCI-002: безопасное подключение live test | B2, E14 | Bind effective checked config + fail-closed DB/caps; dedicated fixture | No production schema; future isolated DB/providers; S–M; deterministic config tests |
| P0 до scientific pilot | SCI-001: узнать usefulness | B3 | Reference set/reviewed frozen sources/metrics/runbook | No migration; experts+approved live environment; M; published review matrix |
| P0 до внешнего доступа | DEPLOY-001: prove Gate B | B4, E19 | Reuse F19 scripts, real operator inputs | No migration; operator IdP/HTTPS/DB/approval; M; actual two-user read/write flow |
| P0 минимум, P1 automation | OPS-001 | B5 | Backup/restore rehearsal, queue/health alert, retention/deletion policy | Deletion changes later may need migration; operator backup/storage; M; restore+two-user graph tests |
| P1 до paid/unattended | SAAS-002: measure/cap actual cost | E09–E13; lifetime/input/usage gaps | Provider metadata/attempt receipt + fixed work budgets + simple reconciliation | Likely forward migration if durable ledger; rate card/provider approval; M; fake failure/repair/crash tests |
| P1 | UX-001: admission/result clarity | E03/E17 length mismatch | DTO/use-case/form length agreement, quota & insufficiency explanation | No migration; no external; S; boundary400/no partial row tests |
| P1 | SEC-001: dependency evidence reconciliation | E18 saved dev HIGH | Verify current advisory/release/reachability, minimal update только separate authorized task | Lockfile change only if justified; registry for future; S; prod gate+full regression |
| P2 | Export enhancement if requested | Print already exists; demand UNKNOWN | Reuse report data; private download if pilot proves need | No initial migration; licence review; S–M; owner/export tests |
| P3 | Billing/organizations/native release | Нет validated demand | Не строить сейчас | External/legal overhead; defer |

SAAS-001 audit уже выполнен в S0, не отдельный launch-blocking код. SAAS-002 analysis здесь выполнен частично; measurement/enforcement остаются будущими. Sizes S/M/L качественные, не календарные обещания.

## 26. Keep / Change / Defer

| Компонент | Решение | Причина |
| --- | --- | --- |
| Domain/Application modular monolith | KEEP | Границы работают, architecture tests есть |
| EF Core/PostgreSQL | KEEP | Durable queue/constraints/real CI; нет причины второй DB |
| Lease/fencing/heartbeat | KEEP | Реальные PostgreSQL negative tests; расширять рядом, не перестраивать |
| Study/discovery/source snapshots | KEEP | Правильное разделение publication vs retrieval provenance |
| PubMed/Europe PMC | KEEP | Достаточно для текущего bounded evidence brief; recall измерить |
| LLM abstraction | SMALL_CHANGE | Usage/input/body bounds и accounting, не новый provider |
| Lifetime stage selection | SMALL_CHANGE | Frozen work set/remaining budget при recovery, без ослабления idempotency |
| Numeric grounding/quantitative artifacts/structured claims | KEEP | Ключевые trust guarantees; не заменять LLM prose |
| Validation duplication | REPLACE_ONLY_IF_PROVEN | Schema, semantic validation, source proof и DB FKs проверяют разные угрозы |
| Next.js BFF/OIDC | KEEP | Token isolation/synthetic release tests; Gate B still pending |
| Tauri | DEFER | Нет подтверждённой native release/customer need |
| Quota/operations | SMALL_CHANGE | Уже есть run/owner/DB; не platform rewrite |

INFERRED maintainability: сложнее всего corpus/source numeric proof и claim/artifact integrity, затем queue/fence и session/BFF. Сначала изучить инварианты и добавить regression tests на изменения; mechanical abstraction ради «одного валидатора» может убрать независимую защиту. Future simplification candidate: одна актуальная карта лимитов/терминов вместо исторически противоречивых README paragraphs, не массовый refactor.

## 27. Приоритетный SaaS roadmap

| Фаза | Outcome / gate |
| --- | --- |
| S0 | Этот audit: evidence inventory, gaps, proposed backlog; no implementation |
| S2a | Минимальные atomic admissions/stop-new-work; safe input/lifetime бюджет для live environment |
| S1 prerequisite | Исправленный SCI-002 harness + isolated DB + reviewer reference design |
| S1 | Tiny approved current live scientific evaluation; collect usefulness/cost/unknowns, no mass run |
| S2b | Actual usage measurement, durable attempt/budget policy; tune caps на данных |
| S3 | F19 Gate B real staging с operator IdP/HTTPS; можно подготовить ресурсы параллельно, не users до проверок |
| S4 | Backup restore, monitoring, incident и retention/deletion minimum |
| S5 | Invite-only web pilot с caps, reviewed limitations/support; measure useful-report rate/retention/demand |
| S6 | Billing только после полезности, стоимости и willingness-to-pay; не автоматически после S5 |

Переупорядочены guardrails перед дорогой live evaluation, потому что текущий harness не доказывает изоляцию/bounds. S3 blocked operator configuration; это не повод внедрять новые auth frameworks. Ни одна фаза не запускается автоматически из S0.

## 28. Первоначальный actionable Codex backlog

### SAAS-001: existing usage, quotas и accounting audit

- Цель: зафиксировать current vs absent usage и отличить scientific bounds от financial guarantees. S0 status: audit выполнен, section13–16; runtime token/cost measurement ещё UNKNOWN.
- Reuse/код: E03/E04/E13, ResearchRun/owner/attempt records/logs; не создавать второй inventory.
- Будущие изменения: согласовать минимальный measurement contract и admissions policy, без billing; если definitions меняются, обновить один current report/ADR.
- AC/tests: каждая usage metric имеет источник/denominator; unknown usage не0; accepted/recovery/terminal определения едины; existing CI regressions сохраняются.
- Prerequisites: выбранная budget policy/model pricing source; P1; зависимости SAAS-002/003 policy agreement; no migration для audit, S.

### SAAS-002: LLM-stage unit economics и retry-cost measurement

- Цель: получить воспроизводимый actual cost и enforced lifetime-call/input bound. S0 уже дал call map/formulas, не measurements.
- Reuse/код: E09–E13, OpenAI structured metadata, repair service, source selection/stage logs, run fence.
- Изменения: reported usage/responseId/outcome/price-version receipts; unknown-cost status; frozen/remaining work budget через recovery; universal input cap и bounded OpenAI body; simple operator totals. Не добавлять billing engine.
- AC: no fake price/token estimates in actual ledger; repair/crash/timeout attempts учтены; новое stage entry не расширяет admitted budget; stale worker не меняет science, receipt не теряется; long abstract не обходит input guard.
- Tests: fake responses с usage/missing usage/error/oversized body; cancellation/repair/crash/reclaim budget; PostgreSQL concurrent reservations; reference math untouched.
- Prerequisites: operator rate card и separate live approval для measurement; P1 до unattended/paid; depends SAAS-001 policy, SCI-002 перед live; likely forward migration для durable accounting; M.

### SCI-001: scientific benchmark и runbook

- Цель: measure current expert accuracy/usefulness, не только Completed.
- Reuse/код: E09–E12 validators/tests, E14 после SCI-002, existing report/provenance/artifacts; cases B01–B12.
- Изменения: human-reviewed versioned references/labels/metrics, bounded offline replay + отдельно approved live evaluation.
- AC: никакой AI answer как gold; per-case correctness/cost/coverage/insufficiency, exact source lineage и cohort linkage; visible false-positive Verified review.
- Tests: deterministic fixture replay, no-network default, opted-out live project не в normal CI.
- Prerequisites: reviewers, legally reusable sources, isolated PostgreSQL, approved provider config/budget; P0 scientific pilot; depends SCI-002 и safe limits; no schema initially; M.

### SAAS-003: minimum atomic owner/global admissions

- Цель: ни один authenticated user/parallel POST не создаёт unlimited accepted backlog. Reuse E03.CreateResearchUseCase/EfResearchStore transaction, owner identity, ResearchRun states.
- Изменения: typed configurable owner/global outstanding/daily limits, global stop, atomic quota reservation + owner-idempotency request/body contract; reject до Question/Run insert; quota-specific error projection.
- AC: concurrent requests не превышают лимиты; UTC rollover определён; same idempotency key/body возвращает тот же run без новой reservation; different body conflict; rejection no rows; recovery no re-reserve; failed/insufficient consumes daily admission; owner isolation сохранена.
- Tests: PostgreSQL multi-context same/different owners, global lock ordering, competing limits, boundary/terminal/crash cases; API/BFF/UI contract; required CI DB skips0.
- Prerequisites: согласованные числа/operator policy; P0; SAAS-001 policy already mapped; external resources не нужны для deterministic implementation, CI Docker есть; likely small forward migration; M.

### OPS-001: backup, monitoring и retention verification

- Цель: user research сохраняется и удаляется предсказуемо, incident можно диагностировать.
- Reuse: E04/E05/E19 schema/health/worker/migration runbooks; owner graph.
- Изменения: minimal backup/restore guide + rehearsal artifacts, RPO/RTO/retention/log access agreement, queue/heartbeat/provider-failure alarms; отдельно согласованный terminal owner deletion workflow.
- AC: restore в isolated DB воспроизводит known report/evidence lineage; no global shared source loss при deletion другого owner; run active/fence policy defined; alerts tested.
- Tests: fake operational checks + PostgreSQL graph deletion tests когда реализуется; actual restore отдельно operator-approved.
- Prerequisites: backup target/access/operator approval/legal retention choices; P0 manual minimum/P1 automation; depends deployment environment for actual rehearsal; migration только если deletion/tombstone design требует; M.

### DEPLOY-001: F19 Gate B

- Цель: реальный external HTTPS/OIDC owner-isolated path доказан.
- Reuse: E16/E18/E19 existing preflight/scripts/runbook; no auth rewrite.
- Изменения: только operator configuration/deployment evidence и fixes реально найденных compatibility defects.
- AC: exact issuer/client/scopes/audience/sub, secure cookies/HTTPS/proxy, two real test users, owner-isolated reads/create, migrations/ready, sanitized artifact report. No synthetic proof substitution.
- Tests: existing synthetic CI + explicitly approved real probes. Live science не требуется для auth verification.
- Prerequisites: operator IdP, hosts/TLS, private DB/secrets and write approval; P0 external launch; depends guardrails before science-enabled public worker; no schema expected; M.

### SCI-002: repair unsafe live-test configuration contract

- Основание нового item: confirmed E14 alias-vs-host mismatch, не новая научная функция.
- Цель/reuse: existing opt-in LiveFactory и ADR-015; гарантировать effective checked config, isolated identity и tiny pre-call bounds.
- Изменения: explicit safe config injection/checks, fail-closed DB identity, bounded stage settings, migration/cleanup policy и assertions separating useful/insufficient result.
- AC/tests: aliases разрешаются ровно один раз; fake host proves actual provider/model/DB/caps; absent/unsafe config не делает HTTP/DB calls; opt-in stays excluded from normal CI.
- Prerequisites: deterministic task без external secrets; live execution позже отдельно authorized; P0 перед S1; no production migration; S–M.

### UX-001 и SEC-001: узкие подтверждённые follow-ups

- UX-001: E03/E17 input length alignment и quota/insufficiency messages. AC: nonblank >1000 → validation400 до DB/LLM; min/max одинаковы form/API; boundary tests. Reuse form/DTO/error contract; P1, S, no migration/external; quota UI зависит SAAS-003.
- SEC-001: E18 current braces advisory/release и historical doc disagreement. AC: dated registry evidence, reachability/patch decision, no suppressed production gate, frozen-lock full regression после отдельного authorized update. P1, S, registry access later, no schema.

Это backlog, а не выполненные изменения. Каждая будущая задача должна снова проверить actual HEAD/dirty files и получить собственную verification.

## 29. Explicit NOT NOW

| Не строить сейчас | Причина |
| --- | --- |
| Microservices / Kafka / RabbitMQ / Kubernetes | Нет измеренной потребности; durable PostgreSQL coordination уже есть |
| Redis/дополнительные databases/search engine | Для первых quotas хватает existing DB; не решает usefulness/cost unknown |
| RAG/embeddings/vector DB/full-text crawler/PDF pipeline | Новая scientific/rights complexity, не текущие blockers |
| Новые statistical methods / automatic inference selection | Existing method input trust ещё требует expert benchmark |
| More LLM agents/providers/model voting | Нет controlled baseline quality/cost comparison |
| Organization RBAC/enterprise tenants | Individual owner model sufficient; collaboration demand UNKNOWN |
| Public developer API | Увеличит abuse/quotas/support/security surface |
| Stripe/invoices/subscriptions/unlimited plans | Demand и actual cost не установлены |
| Public report sharing | Ownership/privacy/licence риск; private print/source links уже есть |
| Native Tauri release / enterprise integrations | Browser pilot first; native runtime не доказан |
| Premature horizontal scaling | Process-local provider quotas и accounting нужно решить сначала |

## 30. Risk register

Качественные likelihood не являются вероятностями; deployment-specific exposure часто UNKNOWN.

| ID | Риск / evidence | Likelihood | Impact | Mitigation / verification | Priority |
| --- | --- | --- | --- | --- | --- |
| R01 | Wrong scientific interpretation при valid tuple, E09/E12, accuracy UNKNOWN | Не измерена | High | Expert cases/false-positive Verified review, SCI-001 | P0 до scientific claims |
| R02 | Unbounded admissions/unknown LLM bills, E03/E13 NOT_IMPLEMENTED | Достижимо authenticated POST | High | SAAS-003 + SAAS-002 ledger/budget, PG tests | P0 |
| R03 | Recovery amplifies work/calls, E09/E10 VERIFIED_IN_CODE | При crashes/source changes | High | Frozen lifetime budget + failure/reclaim tests | P1 до unattended |
| R04 | Wrong live DB/provider через aliases, E14 VERIFIED_IN_CODE | При documented alias-only setup | High | SCI-002 effective-config negative tests | P0 перед S1 |
| R05 | Quota bypass если будущем SELECT/INSERT race | TheoreticalRisk | High | Atomic PG reservation/concurrency, no in-memory counters | P0 requirement |
| R06 | Partial provider/source outage, E07/E08 supported | Возможна | Medium/High | Durable attempts, honest coverage, bounded retry, benchmark outage tests | P1 |
| R07 | Real IdP/TLS misconfiguration, E19 Gate B NOT RUN | UNKNOWN | High | Operator-approved Gate B exact environment proof | P0 |
| R08 | Data loss/no restore evidence | UNKNOWN operational practice | High | OPS-001 isolated restore/report graph | P0 |
| R09 | Sensitive questions/licence/retention issues | Возможны без policy | High | No patient data pilot, legal review, retention/access controls | P0 minimum |
| R10 | Completed пустой report принимается за useful | Supported Insufficient behavior; usefulness UNKNOWN | High trust impact | Product outcome metrics/expert review/clear UX | P1 |
| R11 | Duplicate trial/cohort papers in pool | NOT_IMPLEMENTED trial linkage | Не измерена | Human reference linkage; no independent-study claims | P0 scientific evaluation |
| R12 | Dependency advisory drift/dev tool exposure | Saved dev HIGH, latest UNKNOWN | UNKNOWN reachability | SEC-001 dated audit/patch evaluation | P1 |
| R13 | Unlimited OpenAI body / large abstract input | E13/E07 VERIFIED_IN_CODE gap | Не измерена | Byte/input budget tests, SAAS-002 | P1 |
| R14 | Session revocation/rotation incomplete | Stateless implementation | Возможна | Short expiry, operator logout/rotate runbook, real IdP review | P1 |
| R15 | Complexity/docs drift causes unsafe future edit | Confirmed historical text discrepancies | Возможна | Contract map, focused negative tests, current evidence labels | P1 |
| R16 | No demand / support cost > product value | UNKNOWN | High commercial | Invite pilot usefulness/correction time, no billing launch | P2 commercial decision |

## 31. Простое объяснение для C#/.NET-разработчика

ResearchRun - это одна работа, а не пользователь и не публикация. Status показывает, где работа остановилась; PostgreSQL хранит её независимо от жизни API process.

Worker забирает работу с lease, регулярно продлевает право обработки и сохраняет progress. Если он умер, новый worker продолжит текущую стадию. Version/owner fence не даст старому worker перезаписать новые scientific results. Но внешний LLM уже мог взять оплату: DB transaction не умеет отменить чужой HTTP запрос.

Study - общая публикация. Search/discovery говорят, почему конкретный run её нашёл. Evidence/report принадлежат run и владельцу. Поэтому shared Study не означает shared private report, а несколько discoveries не должны стать несколькими independent participants в meta-analysis.

«До10 Studies в одной стадии» ограничивает сложность научной работы, но не количество работ одного пользователя. Quota нужна перед созданием run. Atomic означает, что два одновременных POST не увидят один и тот же «последний свободный slot» и не потратят его оба.

Unit tests могут доказать, что OR связан с CI в конкретном source pattern и формула HKSJ правильна. Они не доказывают, что population выбрана правильно во всей медицине и report полезен writer. Это проверяют frozen references и эксперт.

Synthetic IdP/LLM tests позволяют воспроизводимо ловить наш код без денег/интернета. Live test проверяет чужой актуальный API и конфигурацию, но дорогой и недетерминированный; без безопасной изоляции запускать его нельзя.

Что автору знать первым: E03 create/admission, E05 queue/fence, E06 stage orchestration, E09 source proof, E11 validated corpus/claims, E13 actual paid generation, E16 owner/auth/session. Остальное читать по изменяемому инварианту, не пытаться переписать всю систему сразу.

## 32. Ровно одна следующая implementation task

**Рекомендация: SAAS-003 — minimum atomic owner/global research admission limits.**

Почему именно она: unlimited authenticated create - подтверждённый непосредственный budget/queue риск; задача реализуема детерминированно на существующем owner/run/PostgreSQL без real IdP, LLM key, live provider calls и новой платформы. SCI-002 остаётся обязательным prerequisite будущего S1, но не второй задачей к запуску одновременно.

Узкий scope: typed owner/global outstanding/daily limits + stop-new-admission; owner-scoped idempotent create; atomic reservation и Question/Run transaction; понятный quota API/BFF/form error; PostgreSQL concurrent/failure/recovery tests. Proposed policy numbers section16 согласовать перед кодом. Не добавлять billing, organization, token-pricing engine, Redis, scheduler rewrite или научные features.

Definition of done: accepted concurrency never exceeds agreed caps; rejected call creates no rows/external work; repeats recover same admitted run; owner isolation/lease/traceability regressions green; required PostgreSQL CI skips0; defaults/env documented; exact limits честно называют admission bounds, не dollar ceiling. S0 не начал эту работу.

## 33. Unknowns, недостающие доказательства и финальная самопроверка

### 33.1. Чего пока не знаем

- Actual model/provider pricing, input/output tokens, external attempt bills, marginal/fixed cost, useful-report cost.
- Expert accuracy/recall/usefulness current post-F16 pipeline; число clinically misleading Verified в реальных cases.
- Real IdP, proxy/TLS/session compatibility и actual F19 Gate B.
- Actual deployment capacity/queue fairness, CPU/RAM/latency, concurrent users.
- Backup storage/restore RPO/RTO, operational alerts/incident process и real secret rotation.
- Licence/region/processing/legal requirements конкретного коммерческого оператора.
- Customer willingness-to-pay, best workflow, demand на team/export/native features.
- Fresh dependency advisories и braces patched release availability на 2026-10-10.

### 33.2. Документация не заменяет код

Подтверждённые расхождения E01/E02/E14/E18–E21: старые passages о purely abstract extraction, отсутствующем forest/quantitative UI, log-only failed search provenance, свободной authoritative narrative, отсутствии browser login/OpenAPI CI уже superseded последующими milestones/code. Они не текущие defects этих функций. Старое обещание live factory bounds расходится с actual E14; dev advisory patch availability расходится с saved audit JSON. Исторические файлы не переписаны S0.

Этот audit также ограничен: все suites не доказывают все инварианты, static code review не production pen-test; per-entry limit и wrong-DB сценарии не воспроизведены на живой системе; benchmark/стоимость не измерены; сохранённые screenshots не новый browser smoke. Я не принимаю ранние «verified» statements без проверки relevant code/test/CI.

### 33.3. Scope и completion

- Baseline, dependency direction, current stages, SaaS capabilities/ownership, LLM sites, bounds/retries и deployment gates проверены.
- Новый local regression recorded; real PostgreSQL evidence отдельно CI на том же HEAD, local skips честно сохранены.
- Scientific correctness разделена на source/structural/numeric/expert; benchmark12 questions без выдуманных ответов/PMID/DOI.
- Economics только formulas/явные сценарные assumptions; missing tokens/cost UNKNOWN; quotas спроектированы, не реализованы.
- Operations/legal/privacy/retention/demand gaps и actionable backlog с одной next task описаны.
- Изменён только этот отчёт; production/tests/config/dependencies/migrations untouched; никаких commit/push.
- Ни paid/live scientific calls, ни opt-in flags, ни external deployment/IdP/DNS/state-changing probes не выполнялись. Только read-only public документация и GitHub CI evidence.

Официальные дополнительные материалы прочитаны лишь для benchmark/legal review flags: Cochrane Chapters4/5, PMC copyright/OA, GDPR official text. Новую проверку текущего NCBI/Europe PMC scientific API контракта или availability этот audit не заявляет. Public documentation browsing не live scientific retrieval.
