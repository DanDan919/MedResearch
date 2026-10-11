# Реальные запуски и ремонт pipeline: 2026-10-11

## Граница проверки

Фактический HEAD: `5590b866121e367572cd3e717b7d29845817d5aa`, ветка main.
Продолжены уже существующие незакоммиченные изменения поиска; сброса истории
или очистки рабочего дерева не было. Новый commit/push требует отдельного
подтверждения; прежний зелёный CI не подтверждает текущий diff.

Пользователь разрешил настоящие исследовательские запросы и удалённую обработку
вопроса/выбранных источников через текущую авторизацию Codex CLI. Использованы
production API composition root, штатный POST, admission/idempotency,
background worker, PostgreSQL и настоящие PubMed, Europe PMC, Crossref.
OpenAI API key не использован; Codex CLI не назван офлайн-моделью.

Стенд: API `http://127.0.0.1:8080`, frontend `http://127.0.0.1:3000`,
отдельная локальная PostgreSQL 18 `medresearch_smoke` на loopback:55433.
История, неудачные запуски и дневные reservations сохранены. Дневной лимит 10
не обходился и не сбрасывался; создавались новые run с UUID Idempotency-Key.

Объём временно ограничен: максимум 3 запроса плана, 20 записей на источник/
запрос, 2 выбранные Study для acquisition/extraction/evaluation/synthesis.
Это проверка исполнения и доверительных границ, не систематический обзор.
Structured full text в этих запусках выключен. Отдельный provider smoke из
предыдущей проверки не выдаётся за полный LLM pipeline.

## Что сломалось и исправлено

### Crossref: HTTP 200 не доказывает релевантность

Контрольный запрос: `vaping electronic cigarette e-cigarette health effect
physiological adverse`, journal-article/has-abstract filter, rows=3, cursor=*.
Без sort получены старые FEBS записи про бактерии, ферменты и ADH со score
около 3-4. С `sort=score&order=desc` получены статьи о vaping со score около
37-39. Это прямой A/B внешнего API, а не тест с выдуманным ранжированием.

Исправление: явно сохранять сортировку score-desc на каждой cursor page.
Оба request-contract теста упали до правки. Все исходные query/filter параметры
сохраняются; next-cursor обновляется. [Официальное объявление Crossref](https://community.crossref.org/t/changes-to-cursors-filtering-and-sorting-in-the-rest-api/16246)
описывает изменения cursors в августе 2026 и нестабильность набора результатов.
Сам score по-прежнему не является научной проверкой включения.

### Evaluation: prompt не выражал требование валидатора

Run `6312af8f-72ca-414c-9769-b39c728293c6` получил 60 записей от трёх
источников, извлёк 3 grounded finding из двух Study и сохранил одну evaluation.
Вторая evaluation отклонена: author-reported limitation отсутствовала в
переданном источнике. Run Failed в Evaluating, report отсутствует.

Отклонённый draft не хранится; точную причину отсутствия цитаты (парафраз,
выдуманное ограничение или другая ошибка) не приписываем модели без данных.
Установленный дефект: v2 prompt говорил о grounding, но не о literal excerpt,
который требовал детерминированный containment. Repair на этапе отсутствовал.

Исправление: `evidence-evaluator-v3-literal-author-limitations`, дословная
непрерывная цитата <=300 символов или `authorReportedLimitations=[]`.
Только `AuthorReportedLimitationNotGrounded` может использовать существующий
бюджет полной замены ответа: default 1, configurable 0..2. Контекст/schema
не меняются, отвергнутый текст не прикладывается, replacement проходит тот же
validator заново. Predicate containment не ослаблен. Ошибки run/study identity,
другие методологические нарушения, provider failure и cancellation не получают
semantic repair. Безопасное сообщение worker не содержит raw model/provider
content. ADR-027 содержит датированное уточнение.

Четыре новых behavioral case упали до исправления; отдельный diagnostic case
упал до исправления worker. Negative controls: wrong identity до/после repair,
zero budget, repeated invalid reply (нет третьего вызова при default budget),
provider failure, cancellation. Все 28 targeted evaluator/worker cases прошли.

## Вопрос 1: вейпы

Повтор через штатный POST: `560816fe-803a-4f41-aa2f-26636cc931d5`.
Вопрос: `the effect of vapes on the body`.

Run Completed, report `f6e0c5d7-a9ee-4353-b431-68ae666fd9b3` Completed,
confidence **VeryLimited**. GET /report и GET /provenance реально прочитаны.
PubMed/EuropePmc/Crossref: по одному SucceededWithResults, 20 записей каждый;
59 discovery paths и 59 distinct Study после identity resolution.
Из двух выбранных Study одна без извлекаемого текста честно Skipped;
одна Completed extraction дала 4 validated finding, одна Completed evaluation.
Synthesis использовал одну Study/4 finding, сохранил 2 StructuredValidated claims.
Planning/extraction/evaluation/synthesis потребовали по одному реальному CLI
вызову; semantic repair в этом повторе не понадобился.

Цитируется реальная `Vaping-induced diffuse alveolar hemorrhage.`, PMID
31956478, PMCID PMC6957782, DOI 10.1016/j.rmcr.2020.100996. Claims сохраняют
EvidenceId -> StudyId -> SourceMaterialId, abstract scope и source coverage.
Отчёт не вычисляет эффект и явно не устанавливает причинность, клиническую
значимость или рекомендации. Одно описание случая и общий background abstract
не являются общим ответом о риске вейпов для всех популяций. Completed означает
завершение исполнения, а не высокую научную достоверность.

## Вопрос 2: недосып и рабочая память

Run `0709e448-8cd5-42a9-84c1-6194b60c708e` создан обычным POST.
Вопрос: `In healthy adults, does acute total sleep deprivation worsen
working-memory performance compared with normal sleep?`
Run Completed, report `63561b65-06f6-4afe-ae17-28562ca212dc` Completed,
confidence VeryLimited: 2 Study, 2 grounded finding, 2 completed evaluation,
2 StructuredValidated claim. GET /report и frontend BFF реально прочитаны.
PMID15140927 (working memory после 24h total SD) и PMID12950703 (cognition/P300
после 38h total SD) разрешаются из persisted Study, не из model identifiers.
Отчёт сохраняет различие outcome/timepoint: отрицательное направление response
times в первой работе и отсутствие ясного reported effect working memory во
второй, не доказательство отсутствия эффекта. Это ограниченный abstract corpus,
не количественный pooled estimate и не ответ о всех вариантах недосыпа.

### Новый обнаруженный пробел: planner syntax

V1 plan явно содержал `"Adult"[MeSH Terms]`. PubMed SucceededWithResults20,
EuropePmc/Crossref Failed/ProviderProtocolError **до HTTP**; это не outage и не
successful zero. Причина: prompt просил PubMed-style запросы, тогда как два
адаптера не обещают перенос controlled vocabulary. Fail-closed сохранён.

Новый `research-planner-v2-portable-keywords` просит keyword/phrase/grouped
AND/OR без field tags, controlled vocabulary, wildcards и NOT/minus. Exclusion
concepts остаются hints, не автоматически выполненными научными фильтрами.
Один prompt-contract test упал до исправления; 43 planner/evaluator/worker
cases и 8 portable query adapter cases прошли. Это руководство модели,
не универсальная гарантия query portability/eligibility.

Повтор с новым планом: `f70115c7-d5bc-45c7-a758-f068a5319ce8`.
Реальный query: `("total sleep deprivation" OR "total sleep loss") AND
("working memory" OR "working-memory") AND (adult OR adults)`.
Все 3 источника SucceededWithResults20: 60 результатов, 60 discovery paths,
56 canonical Study; у 3 Study несколько provenance paths. Run Completed,
report `9c5afd73-7e2a-4e13-9522-bb17210fd2ec` Completed, VeryLimited:
2 completed extraction, 2 finding, 2 completed evaluation, 2 StructuredValidated
claims, те же PMID12950703/15140927 с отдельными current-run EvidenceId.
Одна extraction использует другую immutable SourceMaterial snapshot, не
переписанный старый source. Все 6 CLI вызовов (plan, 2 extraction, 2 evaluation,
synthesis) приняты с первого ответа, semantic repair не понадобился.
Старые планы/отчёты не переписаны. Поиск не означает, что итог цитирует все
источники: оба включённых source snapshot здесь от PubMed.

Read-only проверка всех трёх готовых отчётов сверила 6 citations с run-scoped
Evidence -> Extraction -> SourceMaterial -> Study, PMID/DOI и frontend BFF.
Все ссылки совпали; это provenance/lineage, не универсальное entailment.

## Найденный frontend пропуск

Первый real Chrome smoke ожидал увидеть VeryLimited, но этого текста не было.
Проверка кода установила: экран Research Report вообще не отображал persisted
synthesisConfidence, хотя показывал Completed. Это не изменённая уверенность
backend, а пропущенная видимая ограниченность результата. Шесть unit cases
упали до исправления. Header теперь показывает backend category с читаемым
label; ни score, ни процент, ни новая шкала не вычисляются. Unknown category
сохраняется как исходный текст, без присвоения более высокой certainty; fallback
также проверен на prototype-like строки. Новая browser assertion контролирует
видимость поля. Это внутренняя synthesis category, не формальный GRADE.

После исправления установленный Chrome проверил 3 реальных report на 1440x1000
и 390x844: 6 passed, 0 console/page errors, 0 HTTP/non-cancellation network
failures, horizontal overflow отсутствует. На каждой странице получен настоящий
BFF report200; ответы не mock. Dev StrictMode AbortController отменил 3
начальных GET/page (session, readiness, report); они отдельно отмечены как
ERR_ABORTED cancellations, не скрыты как external failures. Скриншоты desktop/
mobile просмотрены. Ни один browser smoke не создавал новый research request.

## Детерминированная регрессия

Release build: 0 warnings, 0 errors. Полный solution test:

| Проект | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 45 | 0 | 0 |
| Application | 326 | 0 | 0 |
| Infrastructure | 191 | 0 | 0 |
| Integration | 52 | 0 | 134 |
| Всего | 614 | 0 | 134 |

Skip причина: Docker Desktop Linux engine pipe отсутствует. Реальный локальный
pipeline использует native PostgreSQL, но не заменяет весь Testcontainers suite.
EF pending-model: изменений нет. Compose config и git diff --check прошли.
Новая migration/schema не нужна. Нормальные tests не используют внешние
провайдеры/CLI. TRX находятся в ignored TestResults/search-expansion/
live-portable-regression, red и green.

Финальная frontend-проверка: SDK45/Web130 passed, Chrome16 deterministic cases
passed, lint/typecheck passed, Next16.3.8 production build passed. Новый
production OIDC/runtime release harness не запускался; production compile
не выдаётся за deployment verification.

После завершения всех run временный лимит2 возвращён к обычным10 для
acquisition/extraction/evaluation/synthesis. API Release оставлен работающим,
все 3 sources включены с cap20, daily admission limit10 сохранён. /health/live,
/health/ready и frontend BFF ready возвращают200. Final stand logs находятся
в ignored `TestResults/search-expansion/api-local-repaired.log`.

## Оставшиеся ограничения

Больше результатов не означает больше прочитанных, независимых или подходящих
исследований. Выбор bounded work set не является научным relevance ranking;
Study без abstract может занять слот. Score sorting Crossref не воспроизводит
Boolean/MeSH семантику. Цитата подтверждает происхождение, не универсальное
entailment или причинность. Abstract-only corpus не заменяет full-paper review.
Process-local provider gates не распределяют IP quota между replicas.
Персональный NCBI contact email не выдуман; оператор должен настроить свой.

Логи реальных pipeline: ignored
`TestResults/search-expansion/api-live-pipeline.log` (первый Failed) и
`api-live-pipeline-fixed.log` (первые повторы на исправленной Release-сборке),
`api-live-pipeline-portable.log` (portable planning repeat).
Секреты/auth-файлы не читались и не коммитились; raw rejected LLM output
не сохранялся. Новый CI без push не заявляется зелёным.
