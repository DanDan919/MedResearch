# SCI-HF01: контракт synthesis и сохранение частичных результатов

Дата: 2026-10-11. Исправление надёжности, не новая научная функция.

## Исходное состояние и разрешение

Фактический исходный HEAD: `5590b866121e367572cd3e717b7d29845817d5aa`,
ветка `main`, remote `https://github.com/DanDan919/MedResearch.git`.
В дереве были предыдущие исправления поиска, planner и evaluator. Пользователь
разрешил сохранить их отдельным подготовительным коммитом и затем сделать
отдельный SCI-HF01 с push обоих. Подготовительный коммит:
`16898f9738444b42fa93ccd6727f106254f8799b`.
Документ S0 не изменялся; история и исследовательские данные не сбрасывались.

## Независимо подтверждённый сбой

Существующий run `fe069f24-9dcd-470e-8a34-8dad9751c5d8`, вопрос
`the harmful effects of vaping`, проверен по локальному PostgreSQL и журналу
уже завершившегося API. Новое исследование для воспроизведения не запускалось.

| Стадия | Наблюдаемая длительность | Завершённые Codex CLI вызовы |
| --- | --- | --- |
| Planning | 22.624 s | 1 |
| Searching | 4.639 s | 0 |
| Extracting | 872.281 s | 10 |
| Evaluating | 624.528 s | 10 |
| Synthesizing + repair | 334.725 s | 2 |

Начало обработки 12:41:31, завершение с Failed 13:12:30, UTC+05:00.
Итого 23 завершённых CLI вызова. Последние два заняли 155.320 и 179.362 s.
Сохранены 10 Completed extractions, 29 grounded Evidence, 10 Completed
evaluations с `InsufficientInformation`; ResearchReport отсутствует.
Все три источника успешно вернули по 20 результатов: 3 LiteratureSearch,
60 discovery paths, 60 distinct Studies этого run. Глобальные duplicate counts
не доказывают пересечение источников внутри этого run.

Последняя validation error: `Research report claim count exceeds 12.`
Фактическое N исходного отвергнутого ответа не сохранено; оно не выдумывается.
13 и 25 ниже являются специально построенными отрицательными фикстурами.
Историческая публичная причина остаётся `Research processing failed.`:
прошлые записи не переклассифицированы по числу артефактов или догадке UI.

## Корневая причина и эффективный лимит

До исправления static schema рекламировала `claims.maxItems=25`, options и
validator по умолчанию принимали 12, prompt не сообщал числовой ceiling,
а overflow не имел отдельного repairable issue с фактическим количеством.
Это нарушало контракт генерации и делало исправимый overflow неадресным.

Теперь источник эффективного лимита: `SynthesisOptions.BoundedMaxClaims`,
доступный synthesizer через `ResearchReportDraftValidator.MaximumClaims`.
Default остаётся **12**. Существующая явно конфигурируемая политика 1..25
с clamping сохранена, не расширена ради старого отчёта. `Synthesis__MaxClaims`
переопределяет обычную .NET configuration; локальный стенд оставлен на 12.
DI default теперь тоже ссылается на `SynthesisOptions.DefaultMaxClaims`.
Конфигурационные 0/negative отклоняются при startup; positive >25 использует
существующий bounded ceiling25. Отдельный `MaxEvidencePerClaim=12` ограничивает
references одного claim, а не количество claims; он и его schema не менялись.

| Поверхность | Контракт |
| --- | --- |
| Runtime schema | `CreateOutputSchema(effectiveMaximum)`, claims.maxItems равен лимиту validator |
| Default static schema | 12, для совместимости существующих consumers/tests |
| System prompt | `Generate no more than {maximum} scientific claims.` |
| User prompt | `Maximum scientific claims: {maximum}` |
| Validator | То же effectiveMaximum, overflow отклоняется |
| Repair | Actual count N, maximum L, complete replacement с at most L claims |

Ceiling не является целью: запрещено заполнять его выдуманными claims.
Prompt сохраняет противоречия и uncertainty, запрещает удобный положительный
вывод и просит релевантные, подтверждённые, не избыточные claims.
Автоматического `Take(12)` или обрезания ответа нет.

## Structured Outputs и trust boundary

Проверена текущая официальная документация:
[OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=chat).
В основном поддерживаемом subset arrays допускают `minItems`/`maxItems`.
Для fine-tuned models эти ограничения дополнительно не поддерживаются;
strict unsupported schema вызывает provider error. Такая модель не объявлена
проверенной этим hotfix. API-запросы OpenAI для проверки документации не делались.

Existing Responses adapter передаёт schema в `text.format`, strict=true.
Новый keyword не введён: `maxItems` уже использовался, исправлено его значение.
Server validator остаётся авторитетным и при provider, не соблюдающем schema.
Numeric grounding, exact outcome/population/timepoint, current-run references,
mixed-direction handling и deterministic quantitative results не ослаблены.
Schema compliance и наличие citations сами по себе не доказывают научную истину.

Prompt version: `research-synthesizer-v4-bounded-claims`.
Явно разрешённый предыдущий `research-synthesizer-v3-structured-claims`
сохраняет StructuredClaims projection и является idempotency barrier при resume.
Похожий произвольный prefix не становится trusted version. Новые scientific
writes по-прежнему проходят актуальную structured validation; старые отчёты
не переписываются и не генерируются повторно из-за одного version bump.

## Repair и безопасная диагностика

Typed issue `SynthesisClaimCountExceeded`, path `claims`, Repairable.
Feedback содержит только server-authored инструкции и счётчики: actual N,
allowed L, at most L, complete valid replacement. Rejected draft/response,
секреты, stack traces и исходный научный текст в feedback не добавляются.
Replacement проверяется целиком тем же validator и тем же контекстом/schema.
Default repair budget **1**, существующий configurable диапазон 0..2 неизменен.
Повторный overflow после бюджета вызывает safe failure, а не ложный Completed.

Worker публикует фиксированную причину только при Synthesizing и соответствующем
typed issue. Другие ошибки не выдаются за overflow. Progress API добавляет
nullable `failureCategory`: `ReportClaimLimitExceeded`, `ProcessingFailed`, либо
null вне Failed. Категория вычисляется из известной сохранённой safe reason;
схема БД не менялась. Generic historical failures остаются ProcessingFailed.
OpenAPI snapshot, generated TypeScript и Zod сверены с реальным API.

## Частичные данные и UI

Existing owner-authorized `/progress`, `/provenance`, `/quantitative` read paths
не требуют Completed. Failed synthesis не удаляет extraction/Evidence/evaluation.
`/report` сохраняет 409 для известного run без отчёта, 404 для неизвестного/чужого.
UI при 409 дополнительно читает owner-scoped progress того же run; confirmed
Failed/Cancelled не получает обещание «Check back after synthesis completes».
Partial Evidence предлагается только по persisted count, не как готовый report.
При provider/database/read error partial success не выдумывается.

Основной progress теперь имеет три фазы: Finding sources, Analyzing evidence,
Preparing report. Все технические стадии и метрики сохраняются в закрытом
по умолчанию Technical stages. Saved results у Failed/Cancelled означает наличие
артефактов, а не успешную/завершённую scientific stage. Missing stage data не
выдаётся за завершение. Новый ReportClaimLimitExceeded показывает Report unavailable,
без Open report и без несуществующей кнопки Retry.

## Targeted synthesis retry: NOT IMPLEMENTED

Причина конкретная: Domain считает Failed terminal; PostgreSQL queue reclaim
охватывает Queued и active expired leases, не Failed. Нет durable failed-stage
marker, retry counter/ledger и owner-idempotent команды synthesis-only retry.
Write fence требует действующую owner/version lease и active статус. Обычное
UPDATE Failed -> Synthesizing или unfenced direct report save нарушили бы это.

Без этих гарантий невозможно одновременно проверить eligible failure stage,
bounded retries, authorization, concurrency и stale-worker safety. Поэтому
новый endpoint и lifecycle bypass не добавлены, прежний run не сброшен в Queued,
ранние LLM стадии не повторялись. Existing active-stage crash recovery остаётся.
Безопасный future retry должен использовать те же current-run artifacts,
новую атомарную leased attempt и fenced commit одного authoritative report.

Future retry не возвращает прежние расходы; новая synthesis может стоить денег.
SAAS-002 token/cost accounting остаётся отдельным долгом. SAAS-003 admission
не является финансовым потолком и не возвращает reservation после Failed.

## Evaluation cost audit, без изменения стадии

Все 10 source snapshots: Abstract, 629..2386 символов. Full-text acquisition
была отключена. У 7 Studies есть PMCID; это не доказательство лицензии,
доступности/достаточности полного текста. Новых запросов full text не было.
Во всех десяти snapshots отсутствуют проверяемые ключевые tokens blinding и
attrition. Для non-RCT randomization/allocation сохранены NotApplicable.
9 оценок удовлетворяют deterministic правилу abstract + >=3 InsufficientSource
среди шести методологических доменов => InsufficientInformation.
Одна имеет только два таких домена, а её InsufficientInformation сохранена
из принятого model category; она не объявляется forced by >=3 rule.
Rationale не превращается в formal RoB/GRADE и не доказывает scientific accuracy.

Наблюдаемые затраты: 10 calls / 624.528 s. **Доказанно избегаемых calls: 0**.
Нет reviewed comparative experiment, позволяющего удалить эти calls безопасно.
Extraction/Evaluation не объединены и не удалены. Единственный предлагаемый
эксперимент: на одинаковых reviewed abstract/full-text fixtures сравнить
separate vs combined generation, сохранив независимые deterministic validators,
exact source lineage и blinded expected categories; измерить accepted outputs,
ошибки, calls/tokens/latency. До результатов scientific safeguards не менять.

## Red-before-green и проверки

- Claim-limit suite: до исправления 7 failed / 2 passed; после 9 passed.
  Schema/prompt согласованы при 3,12,25; 12 accepted; 13/25 rejected;
  feedback содержит N/L; <=12 repaired replacement принят; повторный overflow
  заканчивается после 2 total calls; fabricated population после сокращения
  всё ещё rejected. Existing zero-evidence test сохраняет InsufficientEvidence.
- Stage-specific safe failure: red 1 failed / 1 passed; green входит в full suite.
- UI red 2 failed / 3 passed; затем дополнены tests mapping всех active статусов,
  incomplete stage data, failed/cancelled report route и отсутствия Open report.
- Новые PostgreSQL cases: 10 Studies / 29 Evidence / 10 InsufficientInformation
  evaluations => 12 StructuredValidated claims, fenced/idempotent report save,
  fresh DbContext read; failed partial owner reads и cross-owner denial;
  v3 report remains current idempotency barrier. Это persisted artificial fixture,
  а не повторное получение 29 scientific findings и не scientific accuracy.
- Existing owner isolation, stale worker writes, leases, F15..F19, M17..M24,
  numeric grounding/provenance/contradictions и OpenAPI drift остаются в full suite.

Локальный final backend: Domain 45, Application 341, Infrastructure 197,
Integration 57 passed / 137 skipped; итого **640 passed, 0 failed, 137 skipped**.
Причина всех 137 skips: недоступный Docker Desktop Linux daemon; эти skips
не считаются доказательством PostgreSQL. CI включает strict
`MEDRESEARCH_REQUIRE_DOCKER_TESTS=true` и блокирует любой required skip.
Результат фактического CI после push должен проверяться, не выводиться из YAML.

Первый полный CI hotfix подтверждён:
[38126867952](https://github.com/DanDan919/MedResearch/actions/runs/38126867952),
commit `2c5d8457f72b72bcc111d584abff79d19984aafc`, **success** во всех трёх jobs.
Ubuntu runner/.NET10, Docker available. TRX annotations: Domain45,
Application341, Infrastructure191, Integration194; **771 passed, 0 failed,
0 skipped**. Значит все137 PostgreSQL cases, включая новые3, выполнены.
EF pending-model, Compose, frontend и actual API/migrated PostgreSQL/HTTPS
browser smoke passed, container builds passed. Normal CI не использует live
scientific providers. Nonblocking warnings: actions v4 Node20 runtime migration
и объявленная будущая смена ubuntu-latest image.

При заключительной DI-сверке обнаружен оставшийся literal default12. Follow-up
использует общий constant и добавляет6 Infrastructure cases: absent/3/25/100
configuration соответствует effective schema, 0/-1 fail startup. Local197
Infrastructure cases прошли. Для этого final follow-up также требуется зелёный
CI; результат последнего run проверяется и приводится в итоговом отчёте,
а success предыдущего commit не подменяет final verification.

Frontend: SDK45, Web141; lint/typecheck/production Next build passed.
Installed Chrome: 18 deterministic cases passed, включая 1280px/390px partial
failure; console/hydration/overflow checks passed, screenshots inspected.
Production OIDC/BFF: 17 passed с синтетическим локальным HTTPS issuer.
UI/desktop packages не имеют unit tests; passWithNoTests не считается coverage.
Restore/Release build passed, 0 warnings/errors. EF: no pending model changes.
Compose config passed; docker info failed по отсутствующему Linux daemon pipe.

При первой сборке live API держал Release DLL (MSB3027). Проверены PID/cmd и
нулевое количество active runs, остановлен только этот API; повторная сборка
успешна. PGDATA и предыдущие run artifacts сохранены. Один новый API-version
test сначала получил 404 из-за отсутствующего run в fake store; фикстура
исправлена, все три whitelist cases прошли. Это test fixture error, не auth bypass.
После rebuild локальный API запущен с прежними normal10-Study bounds и
MaxClaims12. Liveness/readiness/BFF200; GET старого Failed run подтверждает
29 Evidence,10 evaluations,0 reports и исторический ProcessingFailed category.
Новых research POST не было.

## Остаточные ограничения

Новых live OpenAI/Codex/PubMed/Europe PMC/Crossref calls в SCI-HF01 не было.
Работа проверяет контракт/сохранение/UX, не новое живое завершение vaping run.
Abstract-only coverage, incomplete methods, model compliance, fine-tuned schema
compatibility и per-run financial ceilings остаются ограничениями.
Targeted retry не реализован. Scientific algorithms и migrations не изменены.
Секреты и raw provider transcripts не добавлены в документы/фикстуры.

Следующая задача: owner-authorized, durable bounded synthesis-only retry
с explicit failed-stage provenance, атомарной lease/fencing attempt и
PostgreSQL negative/concurrency tests, без повторения предыдущих стадий.
