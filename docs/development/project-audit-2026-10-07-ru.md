# MedResearch: обзор проекта и независимый аудит, 2026-10-07

## 1. Итог

MedResearch - многослойный модульный монолит для поиска научных публикаций,
извлечения Evidence, методологической оценки и формирования traceable ResearchReport.
Есть web-интерфейс, PostgreSQL, durable worker и детерминированная количественная
синтезация. Это не система диагностики и не система назначения лечения.

**Главный вывод:** инженерная регрессия зелёная, но научная надёжность не доказана.
Независимые негативные проверки воспроизвели неправильные `Verified`, объединение
разных вмешательств и сохранение отвергнутой статистики в narrative. Поэтому
source-grounded нельзя интерпретировать как доказанное semantic entailment.

Приоритет: исправить scientific trust boundary и совместимость estimand до
расширения scientific capabilities. Существующие worker leases, PostgreSQL,
multi-source provenance и статистические формулы не нужно переписывать.

## 2. Исходное состояние и метод

- Репозиторий: `E:\MedResearch`.
- HEAD: `0de619c0db344fca162944cb68511d30403bfe69`.
- Ветка: `main`, upstream: `origin/main`.
- Remote: `https://github.com/DanDan919/MedResearch.git`.
- До аудита рабочее дерево чистое; старые milestone HEAD не приняты за текущий baseline.
- Прочитаны repository instructions, architecture/current-state, relevant ADRs,
  project/configuration files, pipeline/persistence/provider/frontend code и соответствующие тесты.
- Применён риск-ориентированный аудит всей карты возможностей, а не заявление,
  что каждая строка, deployment scenario или математический метод формально доказаны.
- Production-код, миграции, зависимости и история Git не изменены.
- Репозиторные изменения: этот новый отчёт и запись наблюдений в `problems.md`.
  Воспроизводимые audit probes находятся вне
  репозитория: `E:\MedResearch-audit-20261007`.
- Реальные OpenAI/Codex/PubMed/Europe PMC/full-text запросы не выполнялись.
  Сетевые обращения для аудита ограничены package advisories, документацией и public GitHub CI metadata.

## 3. Технологии

| Область | Фактическая технология | Замечание |
| --- | --- | --- |
| Backend | C#, .NET 10, ASP.NET Core Minimal APIs | Nullable и implicit usings включены |
| Локальный SDK | .NET 10.0.401, runtime 10.0.12 | `global.json` отсутствует; SDK не закреплён |
| Persistence | EF Core 10.0.11, Npgsql EF 10.0.3 | Explicit `IEntityTypeConfiguration<T>` |
| Database | PostgreSQL 17-alpine | Compose и Testcontainers |
| LLM | `IStructuredLlmClient`, OpenAI HTTP adapter | Ключ/модель проверяются при вызове, не при health |
| Development LLM | Codex CLI adapter | Только Development/ManualScientificE2E; в аудите не вызывался |
| Search | PubMed E-utilities, Europe PMC REST | Provider-neutral Application contracts |
| Full text | Europe PMC structured XML | Bounded acquisition; не PDF/HTML crawling |
| Concurrency | SQL `FOR UPDATE SKIP LOCKED`, leases, owner/version fencing | Не transaction across external I/O |
| HTTP resilience | IHttpClientFactory, token-bucket rate limiting, bounded retries | Limiter общий внутри процесса для соответствующего provider |
| Backend tests | xUnit 2.9.3, Test SDK 17.14.1, coverlet 6.0.4 | PostgreSQL Testcontainers 4.14.0 |
| Web | Next.js 16.3.6, React 19.3.0, TypeScript 5.9.3 | App Router |
| Frontend state/contracts | TanStack Query, Zod, OpenAPI TypeScript | Run-scoped query keys; runtime response validation |
| UI | Tailwind 4, Radix primitives, Lucide | Светлая/тёмная тема, адаптивные workspaces |
| Frontend tooling | Node 24.19.0, pnpm 11.19.0, Vitest 5.0.2, Playwright | Lockfile есть; многие manifest dependencies заданы `latest` |
| Desktop | Tauri 2, React/Vite | Пока shell, не эквивалент web-приложения |
| CI | GitHub Actions, ubuntu-latest, .NET 10.0.x, Node 24.x | Backend + frontend jobs |

## 4. Карта функций

| Функция | Что реально реализовано | Граница/ограничение |
| --- | --- | --- |
| Research creation | `POST /api/research`: Question + Queued Run | POST создаёт новую Question; отдельного rerun endpoint нет |
| Run history | Owner-scoped pagination/status filter | Не глобальный публичный каталог |
| Run observation | Details, persisted progress metrics, lease state | Не streaming scientific payload |
| Planning | Structured PICO-like plan, bounded queries, prompt/provider provenance | Не научные выводы/предписания |
| Multi-source search | PubMed и Europe PMC через один coordinator | Отдельный search/discovery для каждого source/query |
| Study identity | Нормализованные PMID/PMCID/DOI, global canonical Study | Нет fuzzy/title merging |
| Source acquisition | Abstract и Europe PMC structured full-text snapshots | Missing/unavailable сохраняются как ограничения; нет arbitrary PDF retrieval |
| Extraction | Source-aware findings, exact supporting excerpt, numeric grounding facts | Семантическая связь статистики недостаточна: F01 |
| Evaluation | Отдельные categorical methodological assessments | Не formal GRADE/RoB 2/ROBINS-I/AMSTAR-2 |
| Evidence corpus | Run-scoped evidence/extraction/source/study coherence | Anchor revalidation недостаточна: F04 |
| Quantitative readiness | Effect classification, log/Fisher-z normalization, compatible groups | Exposure/timepoint compatibility отсутствует: F02 |
| Quantitative synthesis | OR/RR/HR common/fixed, REML, RE Wald, canonical HKSJ, prediction interval | Не MD/SMD/correlation pooling; нет automatic model selection |
| Quantitative artifacts | Immutable snapshots, contributions, lineage/fingerprint | Формальная точность входного estimand не следует из fingerprint |
| Narrative synthesis | Bounded context, claims with current-run Evidence citations | Citation existence не доказывает истинность текста claim |
| Reports | `GET /api/research/{id}/report`, report/claims/evidence projection | Missing report: honest 404/409 handling |
| Provenance | `/provenance`, searches/discoveries/source metadata/extraction/evaluation/claims/contributions | Raw source content не выдаётся; numeric proof не projected: F09 |
| Web | Dashboard, new research, history, details/progress, report, evidence, quantitative | `/studies` честно обозначает отсутствующий backend API |
| Forest plot | Persisted contributions/summary artifacts | Не выдумывает study-level CI; имеется mobile shell defect: F08 |
| Authentication | Production JWT + owner subject, DevelopmentLocal guard | Web login/token acquisition не реализованы: F14 |
| Health | `/health/live`, `/health/ready`, `/health` | Ready проверяет PostgreSQL, не научные внешние сервисы |
| Desktop | API readiness/link shell | Нативная сборка не подтверждена: F12 |

## 5. Архитектура и фактический путь исполнения

```text
Next.js / API client
  -> ASP.NET Program endpoints + authentication/current actor
  -> CreateResearchUseCase -> IResearchStore -> EfResearchStore
  -> ResearchQuestion + ResearchRun(Queued)
  -> BackgroundResearchWorker [Infrastructure, hosted through API DI]
  -> ResearchRunProcessor
  -> IResearchRunQueue -> PostgreSqlResearchRunQueue [claim/reclaim]
  -> IResearchRunWriteFence -> PostgreSqlResearchRunWriteFence
  -> ScientificResearchStageExecutor [Application]
     Planning     -> ResearchPlanner -> IStructuredLlmClient -> IResearchPlanStore
     Searching    -> ScientificLiteratureSearchCoordinator
                    -> IScientificLiteratureSource [PubMed / Europe PMC]
                    -> IScientificSearchResultStore -> Study/Search/Discovery
     Extracting   -> SourceMaterialAcquirer -> ISourceMaterialProvider
                    -> EvidenceExtractor -> EvidenceExtractionDraftValidator
                    -> IEvidenceExtractionStore
     Evaluating   -> EvidenceEvaluator -> IEvidenceEvaluationStore
     Synthesizing -> EvidenceCorpusBuilder -> SynthesisContextBuilder
                    -> QuantitativeEvidenceAssessor
                    -> FixedEffectQuantitativeStatisticalSynthesizer
                    -> REML / RE Wald / HKSJ / PI -> quantitative artifact store
                    -> ResearchSynthesizer -> ResearchReportDraftValidator
                    -> IResearchReportStore -> ResearchReport + claims + evidence links
  -> ResearchRun(Completed)
```

Dependency direction соответствует layered monolith: Domain без project/package
dependencies; Application зависит от Domain; Infrastructure зависит от Domain и
Application; API является composition root. Architecture boundary regression
прошла. SQL/EF/HTTP transport DTO не используются в Application как persistence
или provider implementations. Размещение hosted worker в Infrastructure само по
себе не нарушает эту зависимость.

Нормальный lifecycle: `Queued -> Planning -> Searching -> Extracting -> Evaluating
-> Synthesizing -> Completed`. `Fail`/`Cancel` допускаются для nonterminal run;
Completed/Failed/Cancelled terminal. Recovery не начинает lifecycle заново:
просроченный active run reclaim/retry выполняется на сохранённой текущей стадии.

Lease: instance id machine/random, acquisition/expiry/heartbeat timestamps,
monotonic lease version. Default lifetime 900 s, heartbeat 60 s, poll 1000 ms.
Claim/reclaim atomic; stage writes fenced в коротких transactions; scientific
I/O вне transaction. Terminal state clears active lease metadata. Host shutdown
не должен становиться scientific failure. Эти гарантии имеют реальные
PostgreSQL tests в CI, но сегодня локально они не исполнены.

## 6. Контракты стадий и persistence

| Стадия | Input -> persisted output | Validation/idempotency | Failure/cancellation |
| --- | --- | --- | --- |
| Planning | Question -> ResearchPlan | Question consistency, bounded queries/types; one plan/run; reuse before LLM | Typed validation + bounded repair; cancellation propagates |
| Searching | Plan queries -> LiteratureSearch/Discovery/Study | Stable identifiers; unique run+plan+source+query for successful search; discovery search+study | Zero results = success; source failure isolated; all sources fail = stage error; failed attempts not durably stored |
| Source/Extracting | Distinct Studies -> SourceMaterial/Extraction/Evidence | Snapshot hash/version, unique supporting anchor, run+study+sourceMaterial+prompt extraction uniqueness | Missing source explicit skip; full-text failure may fall back; cancellation not scientific failure |
| Evaluating | Current-run grounded Evidence -> EvidenceEvaluation | Run+study+prompt uniqueness; categorical states; missing methodology not negative grade | Missing source/evidence explicit skip; typed repair remains bounded |
| Synthesizing | Run-scoped corpus -> artifacts/report/claims/links | Corpus/claim IDs and lineage checks; artifact run+group uniqueness; report run+prompt | Insufficiency explicit; stale/conflicting writes rejected; cancellation propagates |

`MedResearchDbContext` содержит 15 entity sets. 16 forward migrations; latest:
`20261005125712_AddSourceAnchoredNumericGrounding`. Старые migrations не изменены.
PK/FK GUID, nullable missing metadata, string bounds и selected indexes задаются
explicit configurations. PMID/PMCID/DOI имеют unique filtered indexes. Discovery
uniqueness - `(LiteratureSearchId, StudyId)`, не `(ResearchRunId, StudyId)`.
Одно canonical Study может быть найдено несколькими sources/queries; extraction
work set distinct по Study в run.

Нельзя приписывать базе более сильную гарантию, чем она имеет: многие FKs
проверяют существование ID, а same-run/same-study coherence дополнительно
проверяют stores/Application. Не все такие отношения защищены composite database
constraints. Arbitrary direct SQL не эквивалентен использованию production stores.

Metadata merge first-non-null/enrichment, а не ranking providers. Hard identity
conflict между двумя существующими Studies skips merge; null incoming values не
стирают existing metadata. Single matched stable ID + conflicting other non-null
metadata может быть сохранён без overwrite, но не означает доказанное agreement
sources. Global Study/SourceMaterial reuse и acquisition именно в выбранном run
также являются разными утверждениями.

## 7. Статистический слой

Детерминированный Application code, не LLM, вычисляет:

- OR/RR/HR analysis scale `log(effect)`, compatible independent contributions.
- Fixed weights `1 / variance`; common estimate и normal/Wald interval.
- Cochran Q, df и nonnegative Q-derived I-squared над теми же contributions.
- REML tau-squared; отдельные RE weights `1 / (variance + tau-squared)` и Wald result.
- Canonical HKSJ: residual adjustment / `(k-1)`, adjusted summary variance,
  Student-t interval; не modified/ad-hoc `max(1,q)`.
- Отдельный Cochrane-style PI: Student-t df `k-1`, variance `tau-squared + Wald summary variance`.

Regression tests, включая recorded metafor references, прошли. Независимый запуск
R/metafor в этом аудите не выполнен; нельзя называть его новой внешней numerical
verification. Правильная формула не спасает неправильно связанный effect/CI или
разные interventions в одной compatible group.

## 8. Подтверждённые дефекты и пробелы

### F01 [P1] Verified не доказывает связь статистического кортежа

Код: [SemanticNumericGroundingVerifier.cs](../../src/MedResearch.Application/Research/Extraction/SemanticNumericGroundingVerifier.cs#L26),
особенно проверки effect (40), sample size (51), CI (65), confidence level (76),
SE (119), measure (179), number (216).

Реальный validator принял все следующие неправильные candidates:

| Source / adversarial candidate | Actual |
| --- | --- |
| Mortality OR .73 CI .55-.96, infection OR 1.42 CI 1.10-1.85 в одном предложении; candidate .73 + чужой CI | CI 1.10-1.85 retained, Verified |
| Source MD = -.73; candidate +.73 | +.73 retained, Verified |
| `Mortality or infection had HR = 0.73 ...`; candidate OR .73 | Ordinary conjunction `or` accepted as OR |
| `247 participants ... across 63 hospitals`; sampleSize=63 | 63 retained as participants, Verified |
| Mortality .73 / Infection 1.42 в разных предложениях; candidate mortality +1.42 | Outcome и effect independently Verified |
| Selected effect имеет 90% CI, другой effect 95%; candidate confidence=.95 | .95 retained, Verified |
| Mortality OR .73; другой effect SE .21 | Чужой SE .21 retained, Verified |

Причина: co-occurrence по предложению и независимые phrase/token matches, а не
relation/tuple binding; число matching sentences не равно числу возможных
статистических tuple. Exact anchor существовал в реальном source во всех probes.
Исправление: conservative локальный tuple parser/binding; signed numeric tokens;
неоднозначные claims -> Ambiguous/Unsupported. Не поручать upgrade статуса LLM.

### F02 [P1] Разные interventions реально pooled вместе

Код: [QuantitativeEvidenceAssessor.cs](../../src/MedResearch.Application/Research/Quantitative/QuantitativeEvidenceAssessor.cs#L357).
Group key включает outcome/population/comparator/design/measure, но не exposure.
Две uniquely anchored findings `drug A vs placebo` и `drug B vs placebo` для
adults/mortality были приняты как 2 eligible contributions, 1 ready group.
Реальный synthesizer вернул `Synthesized`, pooled OR .73.

Нужен explicit estimand compatibility, включая intervention/exposure и deliberate
timepoint policy. Отсутствующий timepoint нельзя считать совпадающим автоматически.
Это не дефект inverse-variance formula: ошибочна входная совместимость.

### F03 [P1] Отвергнутые числа остаются в narrative

Код: [EvidenceExtractionDraftValidator.cs](../../src/MedResearch.Application/Research/Extraction/EvidenceExtractionDraftValidator.cs#L62),
[ResearchSynthesisPrompt.cs](../../src/MedResearch.Application/Research/Synthesis/ResearchSynthesisPrompt.cs#L367).
Неверный CI .1-.2 стал null в structured fields, но
`ResultSummary = "Mortality OR 0.73 with CI 0.1 to 0.2."` принят без изменения.
Этот ResultSummary входит в synthesis prompt. Наличие source citation не
доказывает numeric content narrative. Финальный LLM report с этим неверным CI
в аудите не генерировался; доказано достижение prompt boundary.

Нужны validation/rejection/repair narrative assertions либо trusted projection,
а не только обнуление numeric columns. Не просто regex удаления всех чисел.

### F04 [P2] Grounding gate и corpus допускают неполные proofs

Код: [EvidenceCorpusBuilder.cs](../../src/MedResearch.Application/Research/Synthesis/EvidenceCorpusBuilder.cs#L176),
[QuantitativeEvidenceAssessor.cs](../../src/MedResearch.Application/Research/Quantitative/QuantitativeEvidenceAssessor.cs#L121).
Прямые Application snapshots с null grounding, Verified facts без anchors,
unknown normalization/out-of-source offsets и Unsupported confidence-level fact
прошли corpus/eligibility. Для unsupported confidence .95 реально вычислен SE
`0.14209827583056575`. Self-hash span проверяется, но source membership повторно
не доказывается; отсутствует проверка обязательного confidence-level proof.

Важная граница: Domain превращает missing grounding в `[]`, и persisted legacy
empty grounding действительно rejected; null bypass не доказан для штатной EF
проекции. Это defense-in-depth hole при неполном/повреждённом Application snapshot,
не утверждение, что публичный API позволяет загрузить arbitrary anchors.

### F05 [P2] Undefined EvidenceDirection принимается

Код: [EvidenceExtractionDraftValidator.cs](../../src/MedResearch.Application/Research/Extraction/EvidenceExtractionDraftValidator.cs#L120).
`Direction="999"` успешно `Enum.TryParse`, accepted enum=999; Domain не проверяет
`Enum.IsDefined`. Проверен реальный draft validator, не live OpenAI ответ.
Нужна closed enum validation в typed trust boundary и domain constructor.

### F06 [P3] Overlapping duplicate anchor ошибочно unique

Код: [SourceAnchorResolver.cs](../../src/MedResearch.Application/Research/Extraction/SourceAnchorResolver.cs#L46).
Source `aaaa`, excerpt `aaa`: occurrences 0 и 1, но resolver вернул Verified.
Second search начинается после полного первого match, пропуская overlapping match.
Это маловероятный scientific text case, но строгая uniqueness гарантия нарушена.

### F07 [P2] Index date Europe PMC становится publication date

Код: [EuropePmcScientificLiteratureSource.cs](../../src/MedResearch.Infrastructure/Literature/EuropePmc/EuropePmcScientificLiteratureSource.cs#L287).
Fake JSON с `pubYear=1998`, `firstIndexDate=2025-03-17`, отсутствующим
`firstPublicationDate` дал PublicationDate=2025-03-17 и PublicationYear=2025.
Официальный [Europe PMC field reference](https://europepmc.org/doc/EBI_Europe_PMC_Web_Service_Reference.pdf)
разделяет дату первой индексации и дату публикации. Нужна отдельная metadata
семантика; не использовать index date как publication fallback.

### F08 [P2] Mobile shell выходит за viewport

Код: [app-shell.tsx](../../frontend/apps/web/components/app-shell.tsx#L36).
Установленный Chrome 154.0.8037.98, production Next: viewport 390x844,
document scrollWidth 575. Все 4 mobile theme scenarios воспроизводят дефект.
Grid/aside min-content размер позволяет nav расширить страницу вместо внутреннего
scroll. Нужен constrained grid/aside (`minmax(0,1fr)`/`min-w-0`) и shell-wide
overflow regression; просто скрывать body overflow недостаточно.

### F09 [P2] Пользователь не видит numeric verification в provenance

`EvidenceProvenanceResponse`/Application provenance projection не включают
NumericGrounding statuses, anchors и PValueOperator. Поэтому persisted
Verified/Ambiguous/Unsupported нельзя inspect в evidence workspace, а условие
`p < .05` теряет оператор в projection. Raw full source при исправлении выдавать
не требуется. Нужен bounded proof read model с safe lineage.

### F10 [P2] Provider failures не являются permanent provenance

Код: [ScientificLiteratureSearchCoordinator.cs](../../src/MedResearch.Application/Research/Literature/ScientificLiteratureSearchCoordinator.cs#L147).
Success/zero-result searches сохраняются, failures - только в logs. При успехе
одного provider stage продолжает работу, но persisted corpus не позволяет
отличить failed source от not-attempted source. Аналогично acquisition failures.
Это known debt, не исправленный новым именем exception. Нужны bounded persisted
attempt/status diagnostics и truthful coverage limitations.

### F11 [P2] Dependency audit имеет 2 high advisories

`pnpm audit --json` exit nonzero: `braces@3.0.3` (eslint/fast-glob dev chain) и
`source-map-js@1.2.1` (PostCSS/Next/Vite/jsdom chains).
Сверены [braces advisory](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) и
[source-map-js advisory](https://github.com/advisories/GHSA-68fv-2mgg-jv7q).
Registry remediation metadata и GitHub patched-version descriptions расходятся;
не выполнять blind override на несуществующий/непроверенный patch.
High scanner severity не означает доказанный удалённый exploit MedResearch:
reachability/untrusted source-map или glob input здесь отдельно не проверены.
Нужны verified dependency updates и полная regression, не production exploit claim.

### F12 [P2] Desktop не имеет подтверждённой native build

`pnpm desktop:build` остановился на `cargo metadata`: Cargo отсутствует.
Дополнительно manifest объявляет `[lib]` без `path`, но `src/lib.rs` отсутствует,
имеется только `src/main.rs`. По [Cargo target specification](https://doc.rust-lang.org/cargo/reference/cargo-targets.html)
default library source - `src/lib.rs`. Это подтверждённая manifest/source
несогласованность; конкретная Rust compiler failure в данном окружении не
проверена. CI строит только Vite shell, не Tauri binary/MSI.

### F13 [P2] Bounds записей не равны bounds HTTP payload

PubMed success bodies читаются `ReadAsStringAsync` без explicit byte limit;
Europe PMC JSON parse также не имеет transport byte cap. Full-text adapter,
в отличие от них, ограничивает XML до 2,000,000 bytes.
Ограничения MaxResults/pageSize не защищают от oversized provider response.
Для ResponseHeadersRead нужен deliberate deadline и на body consumption.
Memory-exhaustion/live slow-stream attack не проводился; это static code gap.

### F14 [P2] Production JWT boundary есть, web token flow нет

SDK имеет `getAccessToken`, но web `createApiClient()` его не подключает.
Login/session/token acquisition отсутствуют. Production API может быть корректно
закрыт JWT, а web без внешней интеграции auth будет получать 401. Это incomplete
production workflow, не обнаруженный authorization bypass.

### F15 [P3] Documentation и contract CI дают избыточную уверенность

README/ARCHITECTURE/current-state/technical-debt всё ещё содержат места «forest
plots not implemented», хотя F7 forest plot существует; некоторые места называют
extraction abstract-only, хотя structured full text существует. ADR-028 фраза о
blocking Frankenstein failure сильнее фактической защиты F01.

OpenAPI generator использует checked-in snapshot; CI проверяет snapshot -> TS,
не live backend -> snapshot. Локально сопоставлены actual OpenAPI paths и shared
schema property sets: research paths/property names не потеряны, но snapshots не
идентичны (health paths manually present, enum/detail constraints отличаются).
Функциональный breaking schema change здесь не доказан; отсутствует автоматическая
гарантия отсутствия будущего drift. UI/desktop test commands с `--passWithNoTests`
не являются доказательством тестирования этих packages.

## 9. Тесты и проверки, выполненные локально

Backend ниже прошёл **и Debug, и Release**, counts не суммируются между builds.

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 28 | 0 | 0 |
| Application | 184 | 0 | 0 |
| Infrastructure | 73 | 0 | 0 |
| Integration | 26 | 0 | 80 |
| Backend total | 311 | 0 | 80 |
| Frontend API (Vitest) | 14 | 0 | 0 |
| Web (Vitest) | 22 | 0 | 0 |
| Existing Playwright Chromium | 11 | 0 | 0 |

UI и desktop unit suites: test files отсутствуют. 80 integration skips вызваны
недоступным Docker Desktop Linux daemon, а не заменой PostgreSQL на InMemory.
Часть Integration tests - API/config/model tests без PostgreSQL, поэтому нельзя
называть все 106 integration tests исключительно PostgreSQL test count.

Дополнительные opt-in проекты собраны/запущены с принудительно false live flags:
LiveE2E 3 skipped, LiveEuropePmcFullText 1 skipped, LiveEuropePmc 1 skipped,
LivePubMed 1 skipped. Это проверка gates и компиляции, **не live verification**.

Independent audit probes:

- Grounding/corpus/grouping harness: 17 checks, 2 controls held, 15 desired
  invariants violated. Это новые audit checks, не 15 упавших штатных xUnit tests.
- Europe PMC fake-HTTP date probe: invariant failed (F07).
- HttpClientFactory fake logging probe: 8 log entries, fake key absent, query
  redacted, 0 actual network requests. Подозрение на default logging secret leak
  отвергнуто, а не добавлено в findings.
- Installed Chrome production smoke: 8 theme/viewport scenarios; theme,
  toggle/reload persistence и runtime/hydration error checks прошли 8/8.
  Layout invariant: 4 desktop pass, 4 mobile fail (один общий F08 defect).

| Command / check | Result |
| --- | --- |
| `dotnet restore MedResearch.slnx` | Passed |
| Debug and Release `dotnet build --no-restore` | Passed, 0 warnings/errors |
| Debug/Release `dotnet test --no-build` | Counts above |
| EF `has-pending-model-changes` | No pending model changes |
| `docker compose config --quiet` | Passed |
| `docker info` | Failed: Linux engine named pipe unavailable |
| `pnpm install --frozen-lockfile` | Passed |
| `pnpm api:generate` | Passed, no generated contract diff |
| `pnpm lint` | Passed |
| `pnpm typecheck` | Passed |
| `pnpm test` | 14 API + 22 web passed |
| `pnpm test:e2e` | 11 passed |
| `pnpm build` | Production Next build passed |
| Desktop `vite:build` | Passed |
| `pnpm desktop:check` | Tool info available; missing Rust/MSVC dependencies |
| `pnpm desktop:build` | Blocked: Cargo missing |
| NuGet vulnerable/transitive audit | No known vulnerable packages reported |
| `pnpm audit --json` | 2 high advisories, nonzero exit |
| `git diff --check` | Passed |

## 10. Coverage и CI evidence

Локально собран XPlat/Cobertura coverage. Объединение по absolute filename + line,
исключены migration/obj/bin generated lines, overlapping assembly measurements
не удвоены. Это local execution coverage, не CI/PostgreSQL coverage:

| Module | Covered lines / instrumented lines | Rate |
| --- | ---: | ---: |
| Domain | 347 / 1197 | 28.99% |
| Application | 5566 / 6559 | 84.86% |
| Infrastructure | 1893 / 5858 | 32.31% |
| API | 925 / 980 | 94.39% |

Низкое Domain/Infrastructure покрытие частично объясняется DB skips, но не
отменяет необходимость прямых invariant tests. Высокое Application coverage
не обнаружило F01-F04: branch execution не доказывает правильность predicate.

Независимо прочитан public GitHub API для exact audited HEAD:
[CI 37408685280](https://github.com/DanDan919/MedResearch/actions/runs/37408685280),
создан 2026-10-06T03:22:40Z, completed/success. Оба jobs ubuntu-latest.
Публичные TRX annotations: Domain 28/28, Application 184/184, Infrastructure
73/73, Integration 106/106, **0 failed, 0 skipped**. Total 391 backend passed.
Docker-required tests реально исполнились; fixture начинает fresh PostgreSQL
container и применяет `MigrateAsync`, MigrationRuntimeTests сравнивает migration
history/current list. Strict skip enforcement присутствует в fixture и workflow.

Это существующий successful CI run на неизменённом code HEAD, проверенный сегодня,
а не новый CI run, запущенный данным аудитом. Новые adversarial probes не входят
в этот green CI и не должны считаться прошедшими в нём.

## 11. Runtime, безопасность и не выполненные проверки

API локально запускался на 127.0.0.1:5199 с worker/migrations disabled:
`/health/live` 200 Healthy; `/health/ready` и `/health` 503 Unhealthy без PostgreSQL.
OpenAPI отдаётся в Development. OpenAI/PubMed health dependencies отсутствуют.
Временные audit API/web servers остановлены; background services не оставлены.

Secret-pattern filename-only scan не нашёл очевидных real credential patterns;
tracked `.env` отсутствует, только example files. Наличие явно development-only
DB password в Compose/test config не является production secret. Это не полный
Git-history secret audit и не pentest. Реальные ключи не запрашивались/не использованы.

Не выполнены/не доказаны: fresh PostgreSQL runtime **локально**, Docker image
build/full Compose up, native Tauri compilation, real JWT issuer integration,
production-auth browser full workflow, live scientific providers, независимый
R/metafor run, нагрузка/soak, complete external-service contract verification,
cohort overlap и semantic equivalence across different wording/timepoints.

Дополнительные static risks для следующего targeted audit: provider limiter
per-process не задаёт общий IP/key quota нескольким API replicas; advisory locks
sorted per candidate не обязательно задают общий batch lock order; широкое
mapping всех InvalidOperationException в HTTP400 маскирует server-state failures;
reported SE не имеет explicit scale semantics для ratio effects. Эти риски не
выданы за воспроизведённые production failures.

## 12. Приоритет доработки

1. F01/F03: failed scientific relation tests перенести в штатную Application
   regression; закрыть tuple binding и narrative channel без weakened validators.
2. F02: добавить exposure/estimand compatibility и fail-closed missing-context
   policy; сохранить M17-M24 формулы и persisted lineage.
3. F04/F05/F09: единый mandatory proof validation, enum membership и bounded
   inspectable numeric proof API/UI; legacy evidence не auto-upgrade.
4. F07/F10/F13: правильные provider date semantics, failure provenance и bounded
   HTTP success consumption; fake contracts остаются deterministic.
5. F08: mobile shell regression across all routes; тема уже не основной blocker.
6. F11/F12/F14/F15: verified dependencies, native target/build checks, real auth
   client integration, truthful documentation и backend-origin OpenAPI CI gate.

## 13. Артефакты и воспроизведение

```powershell
dotnet run --project E:\MedResearch-audit-20261007\Audit.csproj
dotnet run --project E:\MedResearch-audit-20261007\HttpLoggingProbe\Probe.csproj
# Для Chrome script нужен production frontend на 127.0.0.1:3107:
node E:\MedResearch-audit-20261007\chrome-smoke.cjs
```

Grounding probe JSON:
`E:\MedResearch-audit-20261007\bin\Debug\net10.0\audit-results.json`.
Chrome JSON/screenshots: `chrome-results.json`, `chrome-1440.png`, `chrome-390.png`
в том же audit folder. Synthetic fixtures не содержат secrets и не используют
scientific network. Harness intentionally возвращает nonzero, когда desired
invariants нарушены; HttpLoggingProbe печатает явный boolean date invariant.

Штатные TRX и coverage: `E:\MedResearch\TestResults\audit-20261007`,
Release TRX: подкаталог `release`, live gates: `live-gates`. Эти outputs ignored
Git, production files не затронуты. Commit/push не выполнялись: пользователь
запросил анализ и тестирование, не внедрение исправлений.
