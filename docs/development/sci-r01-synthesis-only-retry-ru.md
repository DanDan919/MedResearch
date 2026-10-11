# SCI-R01: безопасный synthesis-only retry

Дата: 2026-10-11. Исходный HEAD `d9216ff0d5cb322785271ffe08393288653f3265`,
main, `https://github.com/DanDan919/MedResearch.git`, дерево было чистым.
SCI-HF01 и S0 документы не переписаны. Пользовательская PostgreSQL БД не изменена.
Никаких live LLM/литературных запросов или новых исследований не выполнялось.

## Eligibility и failure provenance

Worker записывает FailedStage из текущего **сохранённого** status и безопасную
FailureCategory вместе с fenced переводом в Failed/очисткой lease. Domain.Fail
также сохраняет исходную стадию. Новые nullable колонки не backfill исторические
отказы по их тексту или количеству Evidence. Категории bounded/server-authored;
сырые LLM-ответы, stack traces, API keys в retry ledger не сохраняются.

Условия: authenticated owner, Failed, trusted FailedStage=Synthesizing и категория,
нет любого существующего отчёта, нет предыдущей retry-записи, coherent scientific
inputs. План обязан соответствовать вопросу/run. Поиски привязаны к этому плану;
discoveries — к этим поискам/source. Вход не может потерять Evidence/extractions/
evaluations из-за фильтрации чужого Study. EvidenceCorpusBuilder перепроверяет
run/Study/extraction/source lineage и numeric anchors. Повторно проверяются source
hash, supporting quotation, completed same-study evaluation с соответствующими
Evidence IDs, quantitative snapshots/contributions и текущий synthesis contract.
Отсутствующие или несовместимые данные означают Unavailable, не выдуманные замены.

Snapshot + discovery graph + authoritative quantitative artifacts имеют SHA-256
fingerprint. Это не копия источников в retry-таблице. Перед генерацией и при
сохранении report вход повторно сравнивается. Conservative policy отклоняет
изменённые глобальные metadata/source snapshots и несовместимую конфигурацию.
GET eligibility — advisory; только сериализованный POST принимает операцию.

## Durable lifecycle и бюджет

`synthesis_retry_attempts`: ID, уникальный ResearchRunId, RequestedByOwner,
IdempotencyKeyHash, InputFingerprint, Status, requested/started/completed timestamps,
LeaseVersion, GenerationBudget, GenerationCallsReserved, GenerationPending,
FailureCategory. Restrictive run FK сохраняет ledger; CHECK ограничивает statuses
и бюджет. Один owner-requested retry **на весь срок жизни run**, независимо от
ключей/браузерных вкладок/рестартов. Этот initial policy фиксирован, не бесконечный
configurable loop. Результат доступен через GET даже после потери POST response.

Lifecycle: Queued -> Running -> Succeeded / Failed / OutcomeUnknown.
Admission не меняет Failed run; worker claim переводит его сразу в Synthesizing.
Статистические артефакты только переиспользуются, новые на retry не создаются.
Исходные Question/Run IDs и prior-stage outputs остаются прежними.

Call budget = 1 + configured MaxSemanticRepairAttempts: default **2**, диапазон
**1..3**. Бюджет фиксируется при admission. Перед каждым новым dispatch, включая
repair, reservation committed с GenerationPending=true. Вызов невозможен при
exhaustion. Receipt acknowledged до validation; rejected draft не сохраняется.
Настройки repair после admission не могут увеличить durable budget.

Crash до reservation: reclaim может продолжить. Crash после reservation/dispatch,
timeout, cancellation до acknowledgement: вызов потенциально оплачен; неизвестный
исход НЕ refund. Pending при reclaim закрывается как OutcomeUnknown, автоматический
повтор остановлен даже при оставшемся числовом бюджете. Crash после acknowledge,
до report: следующая генерация возможна только в remaining budget; exactly-once
paid requests не обещаны. Crash после report, до lifecycle completion: stage
idempotency barrier переиспользует accepted report без вызова LLM.
Цена/token usage не измеряются; call-count bound НЕ денежный лимит.

## Atomic admission, claim и fencing

POST owner-scoped SQL lock `FOR UPDATE OF run` сериализует все admission requests
этого run. Идентичный key возвращает existing operation до eligibility/budget
checks; другой key получает безопасный 409. Уникальность ResearchRunId в БД —
финальная защита. Нет SELECT-count-then-INSERT вне защищённой транзакции.

Existing `FOR UPDATE SKIP LOCKED` claim добавляет только Failed+Queued-retry intent,
не возвращает run в ordinary Queued/Planning. Claim/reclaim обновляет ledger и
lease в одной транзакции. Heartbeat и owner/version fence прежние. Старая lease
не может acknowledge generation, сохранить report или отметить чужую работу Failed.
Generation transactions короткие; ни один lock не держится во время LLM call.
Run completion + retry settlement атомарны; retry Completed требует report.
Report transaction revalidates complete structured claims и тот же fingerprint;
отвергнутый/частичный отчёт не становится authoritative.

## Исторический vaping run

`fe069f24-9dcd-470e-8a34-8dad9751c5d8` предшествует durable marker. Сохранённые
10 extractions / 29 Evidence / 10 evaluations / 0 reports и generic ProcessingFailed
не доказывают failed stage. Автоматическая eligibility **UNKNOWN / Unavailable**.
Журналы, проверенные SCI-HF01, помогают оператору расследовать отказ, но не являются
существующей trusted database attestation. UUID не special-cased в production.
Real retry не выполнен; никаких historical markers/data не подменено.

Узкая operator-only процедура:

1. Read-only проверить owner, run ID, оригинальный execution log с stage/lease,
   временами и независимым подтверждением Synthesizing failure. Counts недостаточны.
2. Проверить полный current-run graph/anchors/артефакты актуальным validator,
   отсутствие report и любых retry attempts; сохранить bounded audit reference/hash
   исходного журнала, verifier identity, timestamp, rationale вне public API.
3. Получить **отдельное explicit approval** на этот конкретный recovery и новые
   LLM dispatches. Разрешение на разработку не является таким approval.
4. До исполнения нужен отдельный reviewed operator attestation command, который
   сохраняет exceptional verification отдельно от исторического FailedStage и
   использует тот же serialized ledger, generation budget и lease fence.
   Такой override command **НЕ IMPLEMENTED** в SCI-R01. Direct SQL requeue/marker
   backfill недопустимы. Без attestation run остаётся unavailable.

## API, UI и ownership

Authenticated GET/POST `/api/research/{id}/retry-synthesis`. POST: empty JSON `{}`,
UUID `Idempotency-Key`, 202 + Location; неправильный key/body — 400, чужой/unknown
run — одинаковый 404, unavailable/exhausted different key — sanitized 409.
Owner только из JWT sub; payload owner не допускается. BFF сохраняет CSRF/exact
origin/host, private server bearer, allowlisted method/path, bounded body/deadline,
no upstream redirects, private/no-store и session-scoped query caches.

UI кнопка только при verified Available, не по generic Failed/числу Evidence.
Queued/Running/Succeeded/Failed/OutcomeUnknown и Exhausted показываются явно.
Один stable UUID на mounted deliberate action; duplicate click блокируется.
После неоднозначного HTTP ответа можно resend тот же key или refresh durable
status. После reload key не заявляется durable browser storage: GET + per-run
unique ledger всё равно предотвращают второй accepted attempt. Результаты прежних
стадий читаются через существующие owner-scoped Evidence/provenance endpoints.
UI сообщает, что повторяется только report generation и bounded LLM calls могут
стоить дополнительно. Состояние unknown не выдаётся за scientific failure certainty.

## Миграция

Forward migration `20261011093339_AddSynthesisOnlyRetry`: две nullable failure
колонки и минимальная retry table/checks/unique FK. Старые migrations не изменены;
historical failure backfill отсутствует. EF pending-model check: passed, изменений
нет. На пользовательскую local DB миграция в development не применялась.

## Проверка и red-before-green

Observed red: 5 Domain FailedStage cases и anonymous retry route (404 вместо401).
После реализации проходят. Application hooks проверяют reservation before dispatch,
ack before validation, repair budgets0/1/2 и запрет dispatch после отказа reservation.
HTTP tests: owner, чужой GET/POST, invalid/missing/empty key, forged body owner,
anonymous route. Body negative test обнаружил 500 для BadHttpRequestException;
теперь bounded 400, без internal diagnostics.

23 новых PostgreSQL cases в отдельном Testcontainers collection. Ten-study fixture:
10 Study,29 Evidence,10 InsufficientInformation evaluations, initial Failed/Synthesizing.
Прямой/one-repair fake synthesis; claims<=12, один report, прежний run ID и inputs,
owner read/foreign denied. Guards подсчитывают и запрещают planner/search/acquisition/
extraction/evaluation: должно быть0; fake synthesis1 либо2. Это НЕ clinical accuracy test.
Concurrent same/different keys10x, competing workers10x, stale owner, duplicate
persist, crash before/after reservation и после accepted report (0 новых calls), timeout/unknown, exhausted budget, legacy,
wrong failed stages, missing/foreign/corrupt inputs и fingerprint drift.

Локально: restore/build passed, 0 warnings/errors. Domain50 / Application345 /
Infrastructure197 / Integration63 passed; **655 passed,0 failed,160 Docker skips**.
Docker Linux daemon pipe недоступен; SQLite/InMemory/native user DB не подставлены.
Required runtime confidence зависит от strict CI с реальными Testcontainers.
Frozen install, generation, lint/typecheck, SDK45/Web157 tests, production Next
build и17 synthetic production-OIDC browser cases passed. UI/desktop packages
без собственных unit tests не засчитаны как покрытие. Chrome recovery fixtures
используют fake HTTP, никаких реальных provider calls.
Initial Chrome run:18 pass/2 failure из-за Next route-announcer второго alert;
locator ограничен recovery region, полный повтор **20 passed,0 failed**.
Security policy5 / offline deployment40 tests и desktop Vite build passed.
Desktop/mobile screenshots проверены: recovery states и partial navigation
видны, новая панель не создаёт горизонтального overflow.
Compose config и git diff --check passed. CI/финальный commit пока pending.

## Ограничения

Нет monetary cap, token accounting, provider exactly-once или автоматического
operator override. Create daily reservations остаются прежними: retry не создаёт
нового Question/Run/admission и не возвращает дневную квоту. Это отдельная safety
политика; retry capacity/commercial spend controls не реализованы этим milestone.
Fingerprint консервативен и может блокировать valid recovery после metadata
enrichment/configuration changes. Privileged manual DB changes/ledger deletion
вне гарантий. Scientific validators проверяют lineage/structured support, не
универсальную клиническую истинность/causality/полноту abstracts.

Рекомендуемая следующая задача: reviewed operator attestation для legacy
synthesis failure с отдельным explicit live approval, без historical marker forgery.
