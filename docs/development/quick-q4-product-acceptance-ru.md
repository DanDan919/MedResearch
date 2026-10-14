# QUICK-Q4: продуктовая проверка и границы готовности

Дата: 14.10.2026. Это отчёт о детерминированной проверке продукта, **не отчёт о live-клинической верификации**.

## Исходное состояние и изоляция

- Фактический исходный HEAD: `46f29a61b7fe00b4d664340e52ea9963e7034d3e`, `main`, remote `https://github.com/DanDan919/MedResearch.git`.
- Основное дерево содержит параллельные Deep/M24/M25/M26/UI/audit изменения. Q4 реализован и проверен отдельно в `E:\MedResearch-Q4-verify` на указанном committed baseline. Эти проверки не сертифицируют незакоммиченные параллельные изменения.
- Старые коммиты, пользовательские файлы, существующие PGDATA, роль и сервер PostgreSQL на 5432 не сбрасывались, не очищались и не менялись.
- Пользователь разрешил создание нового локального PostgreSQL и commit/push только Q4. Разрешения на live/paid Q4 не было.

## Реальная причина readiness 503

У прежнего локального API на 5252 `/health/live` отвечал 200, `/health/ready` 503. Прямое соединение Npgsql с его development configuration показало **28P01: password authentication failed**. PostgreSQL был запущен; ошибка не являлась результатом недоступного Docker.

Создан новый отдельный кластер PostgreSQL 18.3: `127.0.0.1:55434`, база `medresearch_quick_q4`, роль `q4_local`, PGDATA `E:\MedResearch-local-q4\pgdata`. Случайный development password находится только в защищённом ACL файле `E:\MedResearch-local-q4\local-runtime.json`, вне репозитория. SCRAM-аутентификация. Новая база получила все 24 существующие EF migrations. Q4 не вводит таблиц, колонок или новой миграции.

`scripts/Initialize-QuickLocalPostgres.ps1` отказывается трогать неотмеченный существующий каталог или занятый порт. Повторный запуск сохраняет данные и запускает только собственный отмеченный кластер. Автоматического reset/repair/очистки нет. Миграция является отдельной явной операцией acceptance harness.

Восстановленный отдельный тестовый сервис: API 5254, web 3004; `/health/live`, `/health/ready` и readiness через BFF проверены: **200**. Старый сервис 5252 не перенастраивался; его конфигурационная ошибка не объявляется исправленной. Новый сервис привязан к новой БД и не смешивает её с пользовательскими данными.

## Что теперь работает в браузере

- `/quick-research/new`: authenticated форма, вопрос 1..2000 символов, submit и переход к фактическому run ID.
- `/quick-research/{id}`: существующий Q3 read workspace, ответ, finding, excerpt, metadata, ссылка, ограничения и технические детали.
- `/quick-research`: owner-scoped история и переход к созданию вопроса.
- Browser POST проходит через настоящий Next BFF, серверный bearer, API JWT middleware, PostgreSQL и штатный Quick worker. API responses не подменяются Playwright mock.
- Double-click в одной форме вызывает один POST. Unmount/account change отменяет запрос и не позволяет stale response заполнить чужой cache или выполнить переход.
- При неизвестном результате POST нет автоматического retry: форма блокирует повтор и направляет в историю. Явный 4xx rejection позволяет исправить вопрос.

Контракт BFF: `POST /api/backend/api/quick-research`, JSON `{ "question": "..." }`; API возвращает 202 + `QuickResearchDetails`, безопасный Location переписывается на BFF. GET detail/history остаются private/no-store. POST detail, неизвестные поля, query parameters создания, чужой/null/missing Origin отклоняются. Owner не принимается из формы/headers. Quick API **не поддерживает creation Idempotency-Key**: заголовок не изображает несуществующую гарантию. Deep admission/key logic сохранён.

## Дефекты, найденные actual E2E

| Приоритет | Дефект | Исправление / доказательство |
| --- | --- | --- |
| P0 | Q3 не давал создать Quick через браузер: BFF GET-only | Форма, SDK POST, bounded BFF; реальный POST→worker→PG→answer→history |
| P0 | Локальный PostgreSQL readiness 503 из-за 28P01 | Новая разрешённая отдельная БД, явные migrations и healthy отдельный сервис; старая DB сохранена |
| P1 | SDK требовал RFC-versioned UUID, но source IDs являются SHA256-derived .NET Guid | `guid()` вместо `uuid()`, запрет Guid.Empty, unchanged run/reference membership; non-RFC regression и actual browser |
| P1 | Quick отсутствовал в auth proxy route matcher | Quick navigation включена; anonymous redirect + настоящий User B JWT/404 |
| P1 | Ошибка загрузки БД могла выглядеть как неопределённая ошибка списка | Явный Service unavailable; настоящий stop/resume PG, не fake health |

Пример реального сохранённого ID, отклонявшегося прежним SDK: `4c7be590-0016-1a80-0ae4-3a8a343b4f92`. Fixtures Q3 использовали RFC UUID и не ловили несовместимость. Не изменены алгоритм backend ID, persisted IDs, scientific validators или cross-run guards.

## Acceptance harness: какие части настоящие

Настоящие: production Next build, Kestrel API, JWT validation, owner mapping, EF Core, migrations, PostgreSQL, существующий Quick processor/worker, durable reservations, retrieval/answer validation и persisted reads из нового DbContext.

Подменены только `IQuickScientificSources` и `IStructuredLlmClient` в **test assembly**, не в production API. Источники имеют логические имена PubMed/EuropePmc, но HTTP к ним отсутствует. Их один одинаковый synthetic PMID даёт один source и два discovery. LLM provider `DeterministicFake`, model `q4-fake-v1`, prompt `quick-answer-v1`, один synthetic finding с exact abstract excerpt. PMID/ссылка синтетические: внешний сайт не открывался и существование такой публикации не подтверждалось.

API HttpClientFactory запрещает non-loopback HTTP. JWT backchannel разрешён только к локальному synthetic issuer с проверкой hostname и собственного CA. Браузер actual fixture продолжает настоящие loopback requests, не создаёт поддельные API responses. Windows-only локальный HTTPS fixture использует исключение для synthetic certificate; Linux CI использует доверенный fixture CA без TLS bypass. Это не тест реального production IdP.

Effective acceptance bounds: один query, две fake provider executions, максимум 8 candidates/provider, 5 selected sources, один generation, answer timeout 2 секунды; Deep worker выключен. Не отключены production output validators. Tokens/cost не измерены: `null/unknown`, не «estimated zero».

## Локальные actual сценарии

**11 passed, 0 failed, 0 skipped**, 10 submitted runs:

1. Desktop 1280: login→двойной click→один POST→answer→source/excerpt→API restart→history.
2. Mobile 390: тот же путь, отсутствие page overflow, проверенные screenshots.
3. Partial outage: disclosure неполного покрытия, сохранившийся ответ.
4. Zero results: Completed / InsufficientEvidence, без findings и без generation.
5. No abstracts: InsufficientEvidence, metadata не превращается в доказательство.
6. Оба provider failed: Failed, не «нет научных данных».
7. Invalid JSON: Failed, нет authoritative answer.
8. Foreign source citation: Failed, не persist invalid answer.
9. Timeout: OutcomeUnknown; restart не отправляет generation повторно.
10. Настоящие независимые JWT User A / User B: чужой run/source 404, история B пуста, anonymous redirect, CSRF rejection.
11. Настоящая недоступность PG: live 200, ready 503, UI Service unavailable вместо empty history/spinner; после восстановления ready 200.

После browser runner новый DbContext проверяет **все десять новых runs**: два фактических fake search calls/run, generation calls точно совпадают с persisted AttemptCount (0 или 1), terminal state и очищенная lease. Контроль относится к фактическим provider-events, не только к API полю. Исходные Q2 crash-before-dispatch, reservation collision, expired lease, stale writer и no-paid-redispatch tests сохраняются и запускаются CI.

Полный synthetic short answer/finding: “The synthetic source describes sleep deprivation and memory in adults; this is not a medical answer.”

Exact source excerpt: “This synthetic abstract describes sleep deprivation and memory in adults; it does not report a medical conclusion.”

Contradictions: пусто, не invented disagreement. Uncertainty VeryLimited, явные abstract-only/неполный обзор/lineage-not-entailment/не diagnosis ограничения. Вердикт медицинской поддержки каждого synthetic finding: **cannot verify**. Это проверка тракта и provenance, не медицинский gold test.

## T01–T08: честный scorecard

**Количество live source+LLM cases: ZERO.** Для каждого case ниже: live NOT RUN; полного медицинского ответа и selected sources нет; claim-level support cannot verify; contradictions/limitations/runtime/tokens/cost неизвестны; dispatched live attempts 0. Внешние reference URLs из задания не объявлены автоматически прочитанными или найденными Quick.

| ID | Вопрос | Проверка только текущего query preparation |
| --- | --- | --- |
| T01 | Помогают ли антибиотики при обычной простуде? | RU: пустой план, требуется clarification |
| T02 | Предотвращает ли витамин C простуду или помогает выздороветь быстрее? | RU: пустой план |
| T03 | Are electronic cigarettes safer than smoking, and are they harmless? | EN: шесть AND clauses, включая `than`/`they`; relevance/coverage не доказаны |
| T04 | Как недосып влияет на способность запоминать информацию? | RU: пустой план, несмотря на распознанный недосып |
| T05 | Кофе продлевает жизнь? | RU: пустой план |
| T06 | Уменьшает ли креатин тревожность? | RU: пустой план из-за неизвестной лексики |
| T07 | Лечат ли магнитные браслеты вирусные инфекции? | RU: пустой план |
| T08 | Does vitamin C prevent the common cold? | EN: `"c" AND "cold" AND "common" AND "prevent" AND "vitamin"`; relevance/coverage не доказаны |

Проверен реально вызванный `QuickQueryPreparation.Prepare`, без запросов к providers. Это **не** проведённые медицинские тесты. Не добавлена silent translation и не расширен словарь в acceptance milestone. `control-question-scorecard.json` содержит точные планы и nullable поля каждого case.

## Локальные проверки

| Набор / команда | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 62 | 0 | 0 |
| Application | 437 | 0 | 0 |
| Infrastructure | 234 | 0 | 0 |
| Integration | 67 | 0 | 182 |
| Frontend API SDK | 77 | 0 | 0 |
| Web unit | 206 | 0 | 0 |
| Development Chromium (Deep + Quick) | 27 | 0 | 0 |
| Production synthetic OIDC/BFF Chromium | 19 | 0 | 0 |
| Actual Quick API/PostgreSQL Chromium | 11 | 0 | 0 |

Restore/build, frontend lint/typecheck/production build, OpenAPI regeneration comparison, EF pending-model check, Compose config и diff whitespace check прошли. Backend build: 0 warnings/errors. Локальные 182 skips относятся к существующему Docker/Testcontainers набору: Docker Desktop Linux daemon unavailable. Native-PG acceptance не выдаётся за выполнение всех этих 182 tests.

## CI и артефакты

Q4 расширяет existing `MedResearch.WebReleaseTests` harness: сначала прежний Deep full-stack smoke, затем отдельный Quick actual API/PG browser suite на той же свежей Testcontainers БД. Новая general-purpose job infrastructure не создавалась. Строгий `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true` и zero-required-skip gate сохранены.

`.github/workflows/ci.yml` добавляет upload `TestResults/QuickQ4`. Там: immutable saved run snapshots, provider-events JSONL, control-question scorecard; Playwright сохраняет полные фактические результаты, длительности, screenshots и failure traces. Артефакты используют только synthetic questions/users/sources и не содержат credentials.

Q4 feature commit: `24ece7b20ba97eebdf47c23b2bf815b504b0c827` (`feat: verify Quick research product flow`), push в `origin/main` выполнен. Незакоммиченная параллельная работа в основных файлах сохранена; staged patch был побайтно сопоставлен с isolated Q4 patch, без Deep изменений.

**Проверен фактический CI [38533854331](https://github.com/DanDan919/MedResearch/actions/runs/38533854331): SUCCESS**, все три jobs зелёные. Runner `ubuntu-latest`, фактический image Ubuntu 24.04, SDK 10.0.402, runtime 10.0.13, Docker доступен. Это проверка нового Q4 commit, не перенос доверия с Q3.

- Backend TRX: Domain 62, Application 437, Infrastructure 234, Integration 249; **982 passed / 0 failed / 0 skipped**. Все 182 Docker-required PostgreSQL tests выполнились реально, не были пропущены.
- Fresh Testcontainers PostgreSQL 17: all existing migrations, EF pending-model check и Compose config прошли.
- Frontend logs: SDK 77, web unit 206, development Chromium 27, production OIDC/BFF Chromium 19; все passed.
- Actual API/PG trusted-HTTPS job: **17 existing Deep/full-stack tests + 11 Quick tests passed**, без TLS bypass в Linux; fresh-context dispatch check десяти Quick runs прошёл.
- API и web Docker builds прошли. Required skips: 0. Live source/LLM requests: 0; test fakes не заменяют live clinical verification.
- Q4 CI fixes после первого push не потребовались. Есть non-blocking предупреждение actions v4 о Node 20 compatibility и уведомление о будущей смене ubuntu-latest image; workflow не менялся ради несвязанных обновлений.

Отдельный local development demo также проверен Chromium после запуска: форма 1280/390 без page errors/overflow, настоящий BFF POST 202, PostgreSQL/worker ответ Completed / Succeeded. Source и answer синтетические. Старые пользовательские сервисы и БД сохранены.

## Test inventory: keep / change / remove

- Keep: owner/JWT/CSRF/no-store, URL safety, PG leases/fencing/migrations/collisions, source provenance, excerpt validation, output bounds, zero-vs-failure, unknown-attempt no redispatch, Deep scientific negatives.
- Change: Q3 POST rejection проверяет запрещённый detail POST, а не теперь разрешённый create; добавлен actual hashed Guid, not only RFC fixture; Vitest явно исключает отдельный actual Playwright suite.
- Add: только новый пользовательский create boundary, неизвестный POST, abort/account scope, actual browser→API→DB→worker failures/restart, dispatch audit и DB outage.
- Remove: **ничего**. Unit/BFF/actual browser happy paths не одинаковы: проверяют разные contracts/security/persistence boundaries. Нет обоснования удалять их ради уменьшения счётчика.
- Gaps: реальные медицинские relevance/entailment, внешнее provider availability, русский query coverage, production IdP, создание Quick across tabs/devices/network ambiguity без серверной идемпотентности/admission.

## Локальный тестовый запуск

После frozen frontend dependencies и Release build acceptance host:

```powershell
./scripts/Initialize-QuickLocalPostgres.ps1
# Явное применение existing migrations и actual acceptance:
$env:MEDRESEARCH_QUICK_LOCAL_CONFIG = 'E:\MedResearch-local-q4\local-runtime.json'
dotnet run --project tests/MedResearch.WebReleaseTests --configuration Release -- --quick-local
# Отдельный development-only demo, без live/paid:
./scripts/Start-QuickAcceptanceLocal.ps1
```

Demo URL: `http://127.0.0.1:3004/quick-research/new`, API `http://127.0.0.1:5254`. Подходящий synthetic question: `Does sleep deprivation affect memory in adults?`. Script не останавливает существующие сервисы и отказывается использовать занятые порты. Demo использует existing DevelopmentLocal только в Development, один локальный owner; он не заменяет production JWT isolation тесты. Test fakes остаются в test assembly. Production defaults не переключались.

## Оставшиеся blockers и решение о готовности

- **P0 live-ready:** ни одного реального source-backed, publication-by-publication reviewed medical answer. Live acceptance пункт Q4 не выполнен; Quick нельзя объявлять live-ready.
- **P1:** большая часть RU контрольных вопросов не даёт запроса; EN query preparation может быть чрезмерно буквальным. Fail-closed abstention безопаснее выдуманного перевода, но продуктовая полезность ограничена.
- **P1:** отсутствие backend creation idempotency/admission/cost safeguard. UI guard не защищает multiple devices/tabs и неизвестный committed POST. Один generation/run не ограничивает число новых runs/расходов.
- **P1:** exact citation membership/excerpt подтверждает lineage, не medical entailment. Abstract-only extraction и отсутствие формального certainty framework сохраняются.
- **P2:** оболочка остаётся общим workspace Deep/Quick; mobile navigation горизонтальная. Q4 не создаёт новый design system и не упрощает Deep algorithms.

**Ровно одна следующая задача:** реализовать owner-scoped серверную идемпотентность `POST /api/quick-research` с PostgreSQL race/replay/unknown-response tests. Это отдельная задача, не выполненная автоматически в Q4. Live и расширение научной логики остаются выключены.
