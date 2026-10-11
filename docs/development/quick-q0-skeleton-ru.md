# QUICK-Q0 — изолированный синтетический каркас

Дата: 2026-10-12. Репозиторий: `E:\MedResearch`, ветка `main`.
Исходный HEAD: `6a2f90c5de0155551394157b544ddeed7b6af29c`.
Remote: `https://github.com/DanDan919/MedResearch.git`.

## Граница Q0

Это проверяемый технический vertical slice, **не сервис медицинских ответов**.
Quick не запускает Deep, поиск, extraction, evaluation, synthesis или статистику.
Результат явно synthetic: фиксированное сообщение, пустые facts/disagreements/sources
и ограничения «нет внешнего HTTP/LLM» и «не медицинский совет».
Вопрос не отправляется никакому провайдеру и не включается в worker-логи.

В Q0 нет UI, новых источников, billing, RAG, другого хранилища или framework jobs.
Нет live-provider порта, скрытого fallback либо переключателя на настоящий LLM.
Нельзя считать synthetic completion научным ответом на вопрос пользователя.

## Фактический путь

`POST -> QuickResearchService -> IQuickResearchStore -> quick_research_runs`

`BackgroundQuickResearchWorker -> atomic claim -> QuickResearchProcessor`

`CreateSynthetic -> fenced Complete -> GET owner-scoped result/history`

Domain содержит QuickResearchRun/Status и минимальный source/answer-контракт.
Application содержит use case, store-port и fake processor. Infrastructure содержит
EF mapping, PostgreSQL store, typed options и hosted worker. API содержит только
маршруты/transport/authorization и composition wiring.

## API

Все три маршрута используют существующую JWT authorization policy и `ICurrentActor`.
Владелец берётся из проверенного actor subject, не из body/query. Ответы успешного
Quick API имеют `Cache-Control: private, no-store`.

| Метод | Контракт |
| --- | --- |
| POST `/api/quick-research` | `{ "question": "..." }`; trim, 1..2000 символов; неизвестные body-поля запрещены; 202 + Location + Queued details |
| GET `/api/quick-research/{id}` | details только владельцу; чужой/неизвестный UUID одинаково 404 |
| GET `/api/quick-research?page=1&pageSize=20` | owner-only history; page 1..100000, pageSize 1..50; стабильный createdAt/id порядок |

Без аутентификации — 401. Некорректный question/pagination/body — 400.
Details: id, question, status, createdAt, startedAt, completedAt, safeError, result.
Owner и operational lease наружу не выдаются. History возвращает summaries,
page/pageSize/totalCount, без result payload. GET не запускает обработку.
Canonical Development OpenAPI и generated TypeScript обновлены реальной генерацией.
Quick не добавлен в browser BFF allowlist: браузерный продукт пока только Deep.

## Persisted контракт

Result содержит QuickRunId, IsSynthetic, Summary, Facts, Disagreements, Limitations,
Sources. Source: Id, QuickRunId, Title, optional Url, IsSynthetic. Statement: Text,
SourceIds. Источники встроены в JSONB конкретного root, не являются глобальными Study.
Тестовые synthetic источники используют `example.invalid`; production fake не
создаёт никаких источников, идентификаторов публикаций или медицинских утверждений.

Domain `ValidateQ0` отвергает foreign root/source, duplicate source ID, неизвестную
или повторную citation, non-synthetic result/source и небезопасную URL scheme.
Бounds: максимум 10 элементов на массив; summary 2000, statement/limitation 1000,
source title 300, URL 2048 символов; statement обязан цитировать 1..10 своих sources.
Валидация проверяет структуру/lineage, **не научную истинность или entailment**.

## Database

Forward migration: `20261011190021_AddQuickResearchSkeleton`.
Предыдущие migrations не изменены. Одна таблица `quick_research_runs` в существующей
PostgreSQL: UUID PK, owner (200), question (2000), status (32), UTC timestamps,
safe_error (64), nullable result JSONB, lease_owner (200), lease_expires_at,
lease_version bigint. Не добавлены Deep FK, таблицы публикаций или другая база.

Индексы: `(owner_subject_id, created_at, id)` для history и
`(status, lease_expires_at, created_at)` для claim. CHECK constraints ограничивают
status, coherency terminal timestamp/safe error, lease shape и synthetic root result.
Отсутствующие JSON keys при Completed отвергаются через COALESCE, а не пропускаются
как SQL NULL. Embedded source/citation membership контролируется Domain/store и
owner-filtered read validation, **не PostgreSQL FK внутри JSONB**.

## Async и recovery

Lifecycle: Queued -> Processing -> Completed либо Failed; никаких пяти Deep стадий.
Claim/reclaim — один PostgreSQL statement: eligible candidate, `FOR UPDATE SKIP LOCKED`,
assign owner/expiry, increment version, `RETURNING`. Транзакция не удерживается во время
обработки. DB clock определяет expiry; истёкший Processing повторяет только fake step.
Complete/Fail требуют текущих owner + version + unexpired lease + Processing.
Stale worker не может завершить или fail новый lease. Terminal lease очищается,
Completed/Failed не reclaim-ятся. Повторное Complete возвращает false.

Отдельный узкий worker необходим: существующая Deep queue связана с ResearchRun,
его стадиями и stage-persistence fences. Расширять её ради Quick значило бы менять
Deep. Новый код переиспользует технику PostgreSQL leases, DbContext, DI, hosting и
logging, но не вводит общий scheduler/job framework.

Worker ID — случайный `quick-{Guid}`. Heartbeat в Q0 отсутствует: fake generation
немедленный и не делает внешнего I/O. Если terminal DB write не успел до expiry,
он отклоняется и run может быть reclaim-нут. Перед будущим длинным провайдерным
вызовом lease/admission/cost policy потребует отдельного решения.
Host cancellation не записывает scientific failure: lease истечёт после остановки.
Обычная ошибка записывает только `QuickProcessingFailed`; exception text, question,
source content и secrets не выдаются. При недоступной БД safe failure может не
сохраниться — run остаётся recoverable после expiry, если БД вернулась.

## Конфигурация и безопасный запуск

`QuickResearch:Enabled=false` по умолчанию — выключен только worker, POST может
сохранять Queued. `PollIntervalSeconds=2` (1..60), `LeaseDurationSeconds=60` (5..300).
Все настройки env-overridable через обычную .NET configuration; invalid bounds
отвергаются при DI startup. Для fake run:

```powershell
$env:QuickResearch__Enabled = 'true'
$env:ResearchProcessing__Enabled = 'false'
```

Нужны существующие корректные PostgreSQL/JWT настройки и применённая миграция.
Отключение Deep worker здесь предотвращает обработку его очереди при ручной проверке;
не является изменением Deep production defaults. Q0 не требует OPENAI_API_KEY.
Liveness/readiness остаются прежними; ни один health probe не вызывает провайдеров.
Текущий локальный stand/user database этой задачей не мигрировался и не перезапускался.

## Проверки

Новые Domain cases проверяют lifecycle, bounds, foreign-source/citation и synthetic
boundary. Application cases — empty queue, cancellation, safe-failure port и stale
completion без повторной записи. Infrastructure cases — defaults/config bounds.

Шесть новых реальных PostgreSQL cases используют отдельную collection с существующим
Testcontainers fixture: fresh migrations, structured source/answer reload, owner
history isolation, concurrent claim, expiry/reclaim, stale complete/fail, terminal
exclusion, DB JSON root check и Deep isolation. API case отправляет POST, ждёт fake
completion, пересоздаёт API host с той же PostgreSQL и читает result/history.
Это host-restart persistence regression, не сертификация OS power-loss сценария.
Четыре non-Docker HTTP cases проверяют 401 и malformed/forged input before persistence.
Auth handler тестовый; production использует прежний JWT путь, не тестовый header.

Normal suite не делает новых live HTTP/LLM calls. Testcontainers не заменяется EF
InMemory/SQLite. Локальные Docker skips не являются proof runtime correctness.
CI уже требует `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true` и отвергает любые required skips.

Локальная проверка: restore/build прошли, build — 0 warnings/errors.
Domain: 62 passed; Application: 349; Infrastructure: 202; Integration: 67 passed,
166 skipped. Всего: 680 passed, 0 failed, 166 skipped (нет Docker daemon).
Добавлен 31 Q0 case, включая 6 PostgreSQL и 4 non-Docker HTTP cases.
EF pending-model check: no changes; `docker compose config --quiet`: passed;
`docker info`: failed из-за отсутствующего dockerDesktopLinuxEngine pipe.
Frontend generation/lint/typecheck: passed. SDK: 45 passed. Web: 157 passed,
2 failed — только сохранённые, не включаемые в commit Deep retry tests.
CI runtime результаты пока ожидаются; успех PostgreSQL не заявляется заранее.

## Сохранённая сторонняя работа

`frontend/apps/web/test/synthesis-retry.test.tsx` был dirty до Q0 и сохранён вне commit.
Два его новых красных Deep UI cases не чинятся в Q0; их нельзя выдавать за Q0 regression.
Появившийся `docs/audits/deep_research_quality_audit.md` также не включён в Q0.
Рабочее дерево поэтому не обязано быть clean после Q0 commit; оба файла сохранены.

## Ограничения и следующий шаг

POST в Q0 не имеет idempotency/admission/rate/cost защиты; это изолированный fake
каркас, не production admission для медицинского сервиса. Нет cancel endpoint,
настоящих источников/ответов, scientific evidence validation и web UI. JSONB embedded
lineage не заменяет будущие требования к trustworthy source evidence.

Единственная рекомендуемая следующая задача: **QUICK-Q1 — определить и реализовать
owner-scoped admission/idempotency и явные cost/provider safeguards до любых live
вызовов**. Q1 здесь не реализован.
