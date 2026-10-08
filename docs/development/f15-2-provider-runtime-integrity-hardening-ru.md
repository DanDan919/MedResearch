# F15.2: целостность провайдеров и ограниченное чтение HTTP

Дата: 2026-10-08. Scope: корректировка метаданных, durable search attempts и
transport bounds. Это не расширение научных возможностей.

## Исходное состояние

- HEAD: `3d962ee355b7b3f0b8b486b905f0fe60c3e89376`.
- Ветка `main`, upstream `origin/main`, remote
  `https://github.com/DanDan919/MedResearch.git`; дерево было чистым.
- Прочитаны AGENTS, архитектура, current-state, аудит 2026-10-07,
  F15.1 process report и действующие решения по retrieval/fencing/recovery.
- CODEX_CONTEXT.md в текущем репозитории отсутствует. Его содержание не
  выдумывалось; применены реальные инструкции AGENTS.
- Закрытые F15.1 scientific findings не исправлялись повторно.

## До исправления: контрпример

Первым изменением был fake-HTTP тест
`SearchAsync_IndexDateDoesNotBecomePublicationDate`. До production изменения
он упал: expected PublicationYear=1998, actual=2025. Fixture содержал
pubYear=1998, отсутствующий firstPublicationDate, firstIndexDate=2025-03-17.
Таким образом, дефект подтверждён исполнением, а не только чтением кода.

## Даты публикации

- firstIndexDate полностью исключён из publication mapping.
- Приоритет: firstPublicationDate, затем journalInfo.printPublicationDate /
  electronicPublicationDate, затем pubYear. Известная реальная дата/её части
  выигрывают конфликт с отдельным pubYear: 1998 + 1999-01-02 -> 1999-01-02/1999.
  Это явная precedence policy, не попытка вывести неизвестную дату.
- Year-only остаётся year-only; нет January 1 fallback. Index-only даёт
  null publication date/year. Частичные/некалендарные части фильтруются.
- Study date enrichment теперь принимает только совместимую группу частей.
  Existing year=1998 + incoming date=1999-05-06 не создают противоречивую
  publication date=1999 с publication year=1998.
- Уже сохранённые ошибочные даты не переписываются: невозможно безопасно
  отличить настоящую publication date от старого index fallback без provenance.
- Повторная загрузка official Europe PMC documentation в этом сеансе получила
  HTTP403 от инструмента browsing. Не заявляется новая live contract verification.
  Нормализация исправлена по семантике полей, известной архитектуре и
  deterministic контрпримеру. Scientific provider endpoints не вызывались.

## Реальный dataflow

ResearchPlan query -> ScientificLiteratureSearchCoordinator -> successful-key
reuse check -> BeginAttempt (короткая fenced transaction) -> provider HTTP,
rate gate, bounded retry/body reader -> provider-neutral ScientificSearchResult
-> атомарные LiteratureSearch + Study identity/merge + Discovery + successful
attempt completion. Ошибка provider -> отдельное fenced завершение попытки,
без scientific search/discovery. Затем применяется существующая partial/all
failure policy. Extracting отдельно выполняет SourceMaterial acquisition.

## Logical attempts

`LiteratureProviderAttempt` содержит ID, RunId, PlanId, source (64), query (2000),
StartedAt, CompletedAt?, status, ResultCount?, FailureCategory?, successful
LiteratureSearchId?. Нет XML/JSON, headers, secrets, exception text или stack.

| Status | Scientific meaning |
|---|---|
| Started | Вызов начат, финальный исход не записан; crash/lease loss возможны |
| SucceededWithResults | Provider сообщил положительное число результатов |
| SucceededZeroResults | Successful execution с нулём результатов |
| Failed | Operational/response failure, не научное отсутствие |
| TimedOut | Detectable headers/body/provider timeout |
| Cancelled | Caller/host cancellation, если cleanup безопасно сохранён |

Categories: NetworkFailure, Timeout, RateLimited, InvalidResponse,
ProviderProtocolError, ResponseTooLarge, Cancelled, UnexpectedFailure.
ResultCount не выдумывается для failure. PubMed count описывает возвращённые
PMID, Europe PMC count — нормализованные возвращённые candidates по существующему
контракту; это не размер мировой литературы или число валидных Evidence.

Отсутствие записи не является persisted NotAttempted: исторические searches
предшествуют модели, а enabled-source configuration не хранится как snapshot.
UI явно сообщает отсутствие recorded history, не выводит полноту coverage.

## Recovery, concurrency, fencing

- F9 key `(RunId, PlanId, Source, Query)` сохранён. Успешный search (включая zero)
  не выполняется повторно после stage recovery.
- Неуспешный/незавершённый вызов допускает новую logical attempt с новым ID.
  Предыдущий исход/Started не переписывается. Три HTTP retries внутри вызова
  не создают три literature attempts.
- Attempt success и scientific output commit атомарно. Failure записывается
  отдельной короткой транзакцией перед продолжением/throw stage failure.
- Start/failure/success проходят существующий PostgreSQL run row lock,
  owner + lease version + expiry + active status check. Старый worker не может
  завершить попытку или создать поздний search после reclaim.
- Status — EF concurrency token: две loaded Started snapshots не могут обе
  перезаписать outcome. Finished outcome immutable в Domain.
- Новый attempt проверяет принадлежность Plan тому же Run.
- Нет transaction через HTTP. Нет exactly-once внешних запросов: crash после
  ответа до commit или истечение lease может потребовать повторного HTTP.
- Database/persistence/lease errors больше не swallowed как provider partial
  failure. Они выходят из coordinator и остаются runtime errors.
- При host cancellation используется отдельный cleanup deadline 2 секунды;
  fence остаётся обязательным. При недоступной DB/потере lease сохраняется Started,
  а исходная cancellation всё равно выбрасывается, не становится scientific failure.

## HTTP bounds

| Operation | Inclusive default bytes | Body deadline |
|---|---:|---|
| PubMed ESearch | 256000 | BodyReadTimeoutSeconds=15 |
| PubMed EFetch batch | 2000000 | BodyReadTimeoutSeconds=15 |
| Europe PMC search page | 2000000 | BodyReadTimeoutSeconds=15 |
| Europe PMC fullTextXML | 2000000 | Existing TimeoutSeconds=15 |

Search limits env-overridable через normal .NET configuration и Compose/.env.example.
PubMed search range 1..1000000; fetch / Europe PMC search 1..10000000;
body deadlines 1..120 seconds. Full-text byte cap фиксирован и не ослаблен.

`BoundedProviderBody` использует ResponseHeadersRead, early Content-Length
rejection, чтение maximum+1 byte для неизвестной длины, bounded MemoryStream,
caller cancellation + TimeProvider deadline. Exact limit разрешён, +1 запрещён.
Parsing начинается только после полного допустимого body. Size bounds — bytes
доступного response stream, не wire-compressed size или число символов.
Синхронный parsing ограничен размером входа; отдельного CPU parser deadline нет.

Deadline распространяется на body после headers. Timeout может повторяться
в пределах существующего MaxRetryAttempts (default2, max5). Oversize и malformed
successful JSON/XML не retry. 429/5xx/network/header timeout сохраняют bounded
backoff/jitter/legacy Retry-After cap30s. 400/404 search fail fast; full text
404/403/410 означает unavailable. Non-success bodies не читаются вообще:
зависший error stream не превращает known HTTP failure в зависание.

PubMed ESearch identifiers дополнительно ограничены MaxResults независимо от
размера/честности ответа. Europe PMC malformed structural JSON не равен zero.
HTTP retries и limiter waits cancellation-aware; limiter остаётся process-local,
никакой общей multi-replica/IP quota coordination не добавлено.

## API/UI/ownership

F13 endpoint возвращает providerAttempts одним run-scoped запросом после
owner check. Source bodies и transport transcripts не раскрываются. API mapping,
checked-in OpenAPI и сгенерированные через `pnpm api:generate` TS обновлены.
Zod проверяет source/status/category, UUID, ISO timestamps, nullable counts и
логическую согласованность outcome. Unknown codes reject, не silent fallback.

Evidence workspace показывает нейтральные Succeeded / No results / Failed /
Timed out / Cancelled / Started-outcome-not-recorded. Частичный provider failure
не превращается в API error. Playwright проверяет PubMed success + Europe PMC
failure рядом с сохранённой Study. Нет coverage completeness score, нового
report UI, смены темы или SSR hydration механизма.

Cross-user API отрицательный тест сохранён/расширен; реальная EF projection
отдельно проверяет чужого owner и отсутствие Run A attempts в Run B.

## Source acquisition: сознательная граница

SourceMaterialAcquirer различает failure/unavailable только в runtime logs и
счётчиках; durable run-specific acquisition outcomes отсутствуют. Query/Plan
attempt не соответствует Study/material/version и full-text unavailable.
Поэтому не создана ложная общая сущность: это явно оставленная technical debt.
Full-text transport при этом получил тот же body deadline, не потерял 2MB cap
и сохранил legitimate unavailable/abstract fallback policy. Global SourceMaterial
attribution остаётся отдельной audit finding.

## Tests и validation

- Новый index-date red test выполнен до fix; затем date matrix (real/partial/
  year-only/index-only/missing/conflict/electronic) green.
- Domain: finished attempts immutable, failure не создаёт count, invalid outcome
  reject, coherent date enrichment.
- Application: typed failure beside successful zero, all-source failure,
  successful reuse, timeout classification и persistence failure propagation.
- Infrastructure: ESearch/EFetch/Europe PMC oversized/stalled/malformed success,
  exact inclusive cap, caller cancellation, cancellation gate, bounded body
  timeout retries, non-success body not read, synthetic secret not in exception;
  full-text guard unchanged. Slow tests используют fake TimeProvider, не sleeps.
- PostgreSQL: fresh migrations, attempt status roundtrip, atomic success,
  partial/all failure, cancellation, recovery, cross-run/owner, Plan mismatch,
  stale owner success/failure/start, status concurrency token.
- F15.1 61 cases включены в полный regression; calculators M17-M24 не менялись.
- Frontend: API schema negative tests; coverage states, partial failure,
  API-failure distinction; Chromium scenario + existing hydration regression.

### Локальные точные результаты

Оба regression запуска Debug и Release прошли; totals ниже относятся к одному
запуску, а не к их сумме. Build: 0 warnings / 0 errors; restore passed.

| Project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Domain | 40 | 0 | 0 |
| Application | 252 | 0 | 0 |
| Infrastructure | 110 | 0 | 0 |
| Integration | 26 | 0 | 93 |
| Total normal backend | 428 | 0 | 93 |

Все 93 skips обусловлены недоступным local Docker Desktop Linux engine.
Это не PostgreSQL runtime verification. Отдельный Release запуск F15.1
`ScientificTrustBoundaryCorrectionTests`: 61 passed / 0 failed / 0 skipped.

Live проекты собраны с явно выключенными opt-in flags: PubMed 0/0/1,
Europe PMC 0/0/1, full text 0/0/1, Live E2E/Codex 0/0/3
(passed/failed/skipped). Всего 6 deliberate skips, не включённых в solution
totals. Реальные provider smoke / scientific workflow: NOT RUN.

Frontend API: 24 passed; web: 29 passed; Playwright Chromium: 12 passed,
включая partial provider failure и существующий hydration regression.
Frozen install, API generation, lint, typecheck, production Next build и
desktop Vite build passed. Native Tauri build NOT RUN; отсутствие desktop
unit tests не означает проверку native runtime.

EF pending-model check: no pending model changes. Compose config passed.
Docker info failed с отсутствующим dockerDesktopLinuxEngine pipe, ожидаемо.
Staged diff review и diff check passed; реальные credentials не добавлены.

### CI verification

Production commit: `bd1d28575947d454291ede44f3d933e66ff51b78`,
`fix: harden provider provenance and response boundaries`.
Push в разрешённый origin/main успешен, без force/rewrite.

Workflow `.github/workflows/ci.yml`, runner `ubuntu-latest`, SDK `10.0.x`,
Docker info passed. Run [37735198597](https://github.com/DanDan919/MedResearch/actions/runs/37735198597)
completed **success**. TRX counters получены из GitHub check annotations:

| Project | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| Domain | 40 | 40 | 0 | 0 |
| Application | 252 | 252 | 0 | 0 |
| Infrastructure | 110 | 110 | 0 | 0 |
| Integration | 119 | 119 | 0 | 0 |
| Total | 521 | 521 | 0 | 0 |

93 Docker-backed PostgreSQL cases действительно исполнились (локально они
были skipped). В том числе 12 новых attempt cases: typed outcomes, partial/all
failure, recovery/reuse, cancellation, owner/run isolation, wrong Plan,
stale worker success/failure/start и optimistic status concurrency.
Остальные 26 Integration cases не требуют Docker. Fresh migration application,
EF pending-model check и Compose config passed. CI не заменял PostgreSQL
InMemory/SQLite и не пропускал required database cases.

Frontend job целиком success: generated-type drift check, lint/typecheck,
unit tests, Chromium, production web build и desktop React/Vite build.
Точные frontend counts выше получены локально; CI success не трактуется как
native Tauri verification. Live projects не включены в workflow; scientific
HTTP и structured LLM здесь fake. Никакого OpenAI ключа/paid call не использовано.
CI fixes после первой публикации не потребовались.

Результаты записаны отдельным docs-only commit после production verification.
Финальный HEAD/run проверяются после его push; собственный hash этого документа
не встраивается рекурсивно. Он доступен через `git log` и финальный ответ.

### Проверка обещаний

| Вопрос | Реальная гарантия |
|---|---|
| Может firstIndexDate стать publication date? | Нет в исправленном mapper; historical rows не исправлены автоматически |
| Zero равно failure? | Нет: successful zero и typed operational outcomes раздельны |
| Может partial failure исчезнуть? | Записанный outcome durable; crash/DB loss до completion оставляет Started, а не придуманное отсутствие |
| Oversized success целиком буферизуется? | Нет: reader останавливается на cap+1 до parsing |
| Slow body блокирует caller навсегда? | Нет: отдельный deadline и caller cancellation; общий retry budget конечен |
| Stale worker записывает late result? | Production fenced transactions проверяют current owner/version/expiry; negative PostgreSQL tests passed |
| Научные формулы изменились? | Нет; M17-M24 files не менялись |
| F15.1 protections сохранились? | Все 61 adversarial/control cases passed и входят в CI Application regression |

Отсутствие attempt rows не доказывает NotAttempted: enabled-source snapshot
не сохраняется и исторические runs могут предшествовать этой модели.
Гарантии относятся к обычным production stores/DI; прямое произвольное SQL
администратора не является частью application fencing contract.

Web dev server доступен на `http://localhost:3000/` (HTTP200 проверен).
Это не запуск полного API/PostgreSQL stack: local Docker всё ещё недоступен.

## Security и ограничения

Реальных API keys/secrets не добавлялось. Тестовое sensitive значение synthetic.
Никаких live scientific/Codex/OpenAI calls, paid API или optional smoke здесь нет.
Автоматические HTTP URI loggers отключены для scientific clients; retry logs
не включают raw body/exception. Network exceptions с потенциальным secret URI
заменяются безопасным typed failure без сырого inner exception.

Remaining: process-local quotas; external contract drift/availability; incomplete
source metadata; Started outcome uncertainty; best-effort cancellation storage;
исторические index dates; отсутствие acquisition attempt history и retention/
pagination; global source attribution; мобильный shell overflow, dependency
advisories, native Tauri, production web auth, backend-origin OpenAPI drift gate;
free-text narrative entailment не доказан структурной citation traceability.

Рекомендуемый ровно один следующий milestone: Narrative Claim Grounding,
поскольку numeric tuple/source proof не доказывает свободный научный текст.
Следующий milestone здесь не начат.
