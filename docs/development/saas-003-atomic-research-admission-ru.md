# SAAS-003: атомарный приём исследовательских работ

Дата: 2026-10-10. Репозиторий E:\MedResearch, main,
remote https://github.com/DanDan919/MedResearch.git.

## Исходное состояние и разрешение

S0 baseline: 25667c243b880a71bbbe2c423009886432dfaad3,
CI 37934194980. Единственный untracked S0-отчёт не смешивался с кодом.
После явного пользовательского «потверждаю» он был отдельно закоммичен:
0ed329fbd5bf995e46793d7d27486cbfc2fa92ff,
`docs: record SaaS readiness audit`. Чистая рабочая копия проверена до реализации.
Тестовый red-коммит: 459b5d969ae09e7bcde6c6df2fb5429476d0cac5.
После tooling interruption состояние проверено повторно; частично сохранённые
изменения продолжены, история не переписана, reset/clean/stash не применялись.

## Наблюдённый red-before-green

CI [38039020143](https://github.com/DanDan919/MedResearch/actions/runs/38039020143),
backend check 114175370830: настоящий PostgreSQL/Testcontainers, 158 integration
тестов, 155 passed, 3 failed, 0 skipped. Наблюдались именно такие дефекты:

- 10 параллельных distinct-key запросов одного владельца приняли 10 работ вместо 1;
- четыре владельца через два API-хоста приняли 4 работы вместо глобальных 2;
- повтор после условно потерянного ответа вернул другой Run ID.

Domain 45, Application 289, Infrastructure 110 прошли. Это реальный pre-fix
результат, а не предположение о том, как старый код должен был упасть.

## Что такое admission

Admission - принятое сервером новое исполнение вопроса. Оно атомарно связывает
вопрос, Queued Run и неизменяемую запись квоты/idempotency. Это не результат
научного анализа, не число публикаций и не денежная операция.

HTTP -> JWT subject -> CreateResearchUseCase -> IResearchStore -> EfResearchStore.
Существующий путь создания сохранён; второй независимый create-путь не добавлен.
Domain и scientific pipeline не получили HTTP/EF/quota-реализацию.

## Транзакция и конкурентность

Один короткий READ COMMITTED transaction получает PostgreSQL transaction advisory
lock (1297237323, 1). После ожидания читается актуальное committed состояние:
owner/key replay -> stop -> owner outstanding -> global outstanding -> owner daily
-> global daily -> INSERT Question + Run + admission -> COMMIT.

Глобальный database lock работает между подключениями и API-процессами.
Внутри нет OpenAI/PubMed/Europe PMC вызовов и worker обработки. Исключение,
FK-ошибка или отмена до commit откатывают все вставки и освобождают lock.
Отдельного active counter, Redis или guard-row платформы нет.
Тесты с двумя WebApplicationFactory проверяют независимые DI/DbContext/connection;
дополнительные тесты запускают отдельные OS-процессы API на ephemeral localhost
портах, чтобы не выдавать два хоста одного процесса за межпроцессную проверку.
Последние используют явно development-only identity; production JWT проверяется
другими реальными middleware-тестами, без изменения production авторизации.

## Outstanding и сутки

Outstanding: Queued, Planning, Searching, Extracting, Evaluating, Synthesizing.
Queued уже расходует место: очередь сама по себе будущая работа.
Completed, Failed, Cancelled освобождают outstanding; отсутствующая/истёкшая
lease не освобождает его. Worker reclaim не создаёт вторую admission.

Daily: принятые работы в интервале [00:00 UTC, следующие 00:00 UTC).
PostgreSQL clock_timestamp читается ПОСЛЕ lock, не transaction-start timestamp
и не часы API-реплики. Новые записи используют именно это accepted time.
Legacy Runs без ledger тоже учитываются по их историческому CreatedAt;
миграция не обнуляет квоту и не выдумывает idempotency keys.
После failed/cancelled/completed или insufficient report нет daily refund:
внешний расход уже мог произойти. UTC midnight тестируется без wall-clock sleep.

## Политика

ResearchAdmission:OwnerOutstandingLimit=1,
GlobalOutstandingLimit=2, OwnerDailyLimit=2, GlobalDailyLimit=10,
StopNewAdmissions=false. Все числа 1..10000; owner <= соответствующего global.
Это pilot defaults, а не измеренная научная/коммерческая capacity.
Отсутствующая конфигурация не отключает квоты; invalid config останавливает startup.
Обычные .NET env keys ResearchAdmission__...; Compose aliases документированы
в .env.example. Options фиксируются при startup: изменить и перезапустить ВСЕ
реплики с одинаковой политикой. Mixed-policy rolling deployment не является
атомарной глобальной операторской остановкой.
Stop блокирует только новые admissions, не replay, reads или уже выполняемую работу.

## Idempotency и API

Обязателен non-empty UUID в Idempotency-Key. Scope - authenticated owner,
не browser owner/email/identity header. Canonical body - принятый trimmed question;
внутренняя пунктуация, whitespace, регистр и Unicode не переписываются.
SHA-256: UTF-8 от `research-create-v1\n` + canonical question. Дополнительной
копии вопроса в таблице нет. Fingerprint - не шифрование и не защита от перебора
известного текста. Key не заменяет авторизацию, в логи не попадает.

Same owner/key/body возвращает исходный 201/Queued и Location, включая после
terminal или stop. Текущий status читается GET. Same key/different body -> 409.
Другой owner может использовать тот же key, но получает отдельную работу.
Replay разрешается до квот и не расходует вторую reservation.
Rollback/rejection не расходуют key навсегда. Paid external calls exactly-once
этим механизмом НЕ обеспечиваются.

| HTTP | Public code | Значение |
| --- | --- | --- |
| 400 | admission-invalid-key | ключ отсутствует/невалиден |
| 409 | admission-idempotency-conflict | принятый ключ связан с другим вопросом |
| 429 | admission-owner-outstanding | owner backlog заполнен |
| 429 | admission-global-outstanding | общая capacity заполнена |
| 429 | admission-owner-daily | owner UTC daily исчерпана |
| 429 | admission-global-daily | global UTC daily исчерпана |
| 503 | admission-stopped | оператор остановил новые submissions |

Daily 429 содержит bounded Retry-After до UTC midnight. Outstanding не обещает
известного времени освобождения. Обычная invalid question остаётся 400;
transient DB недоступность сохраняет отдельный operational 503, другие DB ошибки
не маскируются scientific insufficiency. Сообщения не содержат чужих IDs/counts.

## BFF и frontend

Backend-origin OpenAPI regenerated существующим test mechanism; TS SDK generated,
не редактировался вручную. Header required/UUID и 409/429/503 проверяет drift gate.
SDK createResearch требует caller key. BFF проверяет UUID только на разрешённом
create POST, не пересылает его на GET, не принимает browser bearer/owner/cookie.
Origin/CSRF, encrypted private session, allowlist, body cap, private/no-store и
redirect restrictions сохранены. Error JSON ограничен 4096 bytes; разрешены
только известные code/status пары с постоянными публичными сообщениями.

Форма генерирует один key на mounted submission. Same-question retry после
неоднозначного ответа использует прежний key; изменённый вопрос/новая форма - новый.
Ref guard блокирует второй submit до React state update. UI показывает quota,
pause/conflict отдельно от scientific failure. Session reload recovery не
реализован: после reload SDK caller сам должен сохранить исходный key, иначе
это новая submission. В browser storage не добавлены вопросы или credentials.

## База и retention

Новая forward migration: 20261010141328_AddResearchAdmissions. Старые не изменены.
research_admissions: owner_subject_id varchar(200), idempotency_key uuid,
request_fingerprint varchar(64), research_run_id uuid, created_at timestamptz.
PK(owner,key), unique Run FK; индексы created_at и (owner,created_at) обслуживают
global/owner daily windows. Run deletion RESTRICT не даёт обычному cascade стереть
дневную историю/replay. Автоматической purge/retention нет; future deletion
требует явной политики, privileged ручное изменение БД выходит за гарантию.

## Проверки и границы доказательства

Новые PostgreSQL cases: 10 distinct-key owner requests; четыре owners/two hosts;
10 same-key retries/two hosts; отдельные API OS-процессы; canonical trim replay;
cross-owner same key/forged owner header; conflict; invalid keys; stop/read/replay;
owner/global daily; terminal states; exact UTC midnight; все nonterminal statuses;
legacy daily; FK rollback; cancellation до/во время transaction; PK/FK/unique Run;
worker reclaim/stale fence без новой reservation.
Existing scientific, tuple/claim/report, ownership и full-stack gates не ослаблены.
Shared-fixture старые scientific/read tests имеют явные test-only limits 10000;
новые admission/concurrency тесты используют настоящие pilot limits и isolated DB.

Локально: restore/build (0 warnings/errors), EF no pending changes, Compose config
passed; frozen install, API generation, lint/typecheck, Next production build
passed. SDK 42/Web 122 unit tests; Playwright scientific/hydration 15 и synthetic
production OIDC 17 passed. Финальные backend totals и CI outcome записываются
в следующем разделе после настоящего PostgreSQL в CI. Локальный полный backend:
Domain 45, Application 301, Infrastructure 112, Integration 52 passed/132 skipped,
0 failed; суммарно 510 passed/132 skipped. Docker skips ожидаемые, не успех PG.
Offline deployment controls 40 и security policy controls 5 passed. Dependency
audit: production 0 advisories; прежний dev-only HIGH braces остаётся видимым,
не suppress и не production exemption нового пакета (dependencies не добавлены).
Локальный Docker Desktop Linux engine недоступен; skips не доказывают атомарность.
Первый промежуточный local test использовал старую сборку и pre-generation OpenAPI:
stale empty-key assertion и ожидаемый drift были устранены rebuild/generation,
не ослаблением assertions/gates.

CI verification pending на момент implementation commit: COMPLETE пока не заявлен.
Настоящие внешние scientific/paid API не вызывались. Real deployment Gate B и
production IdP этим milestone не подтверждены.

## Остаточные риски и ровно одна следующая задача

Все API replicas должны использовать этот create-путь, одну БД и одну policy;
прямые SQL writers и mixed config обходят системную дисциплину. Advisory lock
сериализует короткий pilot admission, throughput/DoS/wait latency не сертифицированы.
Часы PostgreSQL считаются operational trusted; skew новых API часов не выбирает
дневной интервал, историческая точность legacy CreatedAt не переизобретена.

Admission count НЕ гарантирует dollar budget. Нет token/cost metering,
per-run/global spend ceiling, cumulative Study lifetime bound или exactly-once
paid requests. S0 recovery gap MaxStudiesPerRun не исправлен в этом scope.
Public unlimited live use остаётся финансово неготовым.

Следующая задача: SAAS-002 - измерение LLM usage/cost и ограничение совокупной
стоимости/работы при recovery перед расширением live использования.
