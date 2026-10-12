# QUICK-Q1 — bounded scientific retrieval

Дата: 2026-10-12. Baseline: `ad1e3e6a2434d442bf6e47585c3a640e407ac494`.
Рабочая ветка main, remote `https://github.com/DanDan919/MedResearch.git`.

## Scope и фактический путь

Q1 сохраняет выбранные публикации, **не отвечает на медицинский вопрос**.
Он не запускает Deep Planning/Extraction/Evaluation/Synthesis, статистику или LLM.
Не добавлены full text, новые providers, billing, browser UI или job framework.
Crossref не включён: supplemental metadata не нужна для этого двух-source пути.

`POST -> QuickResearchRun -> claim -> QuickRetrievalProcessor -> QuickQueryPreparation`

`QuickScientificSources -> existing PubMed/EuropePmc adapters -> QuickPublicationSelector`

`fenced retrieval snapshot -> owner-scoped GET/history`

Общий adapter request исторически имеет поле ResearchRunId: в Quick это correlation
ID, не запись/FK Deep. Deep search store не переиспользован: его run/plan fences и
foreign keys не подходят Quick. Authentication, EF, PostgreSQL и Quick worker сохранены.

## Query preparation

`quick-query-v1` выдаёт **один** запрос <=512 символов: AND для topics, явные OR aliases,
population сохраняется. Question bound1..2000; preparation ограничивает tokens/terms.
Нет LLM translation. English words и явный bilingual glossary покрывают выбранные
обычные темы, включая vaping, creatine/memory, sleep/anxiety, antidepressants,
stimulants/long-term scope, magnesium/insomnia и известные population aliases.
Шесть RU/EN equivalence regressions соответствуют примерам product vision.

Это **не универсальный переводчик**. Неизвестный Cyrillic term, negation, несколько
populations или чрезмерный scope -> NeedsClarification без HTTP. Неизвестные terms
не отбрасываются и не переводятся наугад. Generic intent verbs используются как
stop words: сохранение всей семантики произвольного вопроса не доказано.

## Bounds и metadata

PubMed/EuropePmc: до8 candidates на query/provider, до5 selected refs. Сейчас один
query даёт максимум16 mapped candidates. Persisted shape ограничен2 queries,
4 logical slots и32 candidates для bounded recovery, но normalizer выдаёт1 query.
`ScientificSearchRequest.MaxResults` необязателен: min(request, configured cap),
null оставляет прежний Deep limit. Общие rate gates и adapter resilience не меняются.
При default batch/page settings normal query требует до3 HTTP без retries:
ESearch + batched EFetch + Europe search. При иных settings requests больше,
но ограничены candidate/retry/deadline budgets; logical attempts != HTTP retries.

Malformed binding или oversized returned set отвергаются. Metadata limits:
title1000, abstract16000, journal300, authors50, publication types25; oversized
records учитываются как rejected. Abstract не обрезается/не выдумывается;
missing abstract допустим. Partial date не превращается в January1. Month-only
precision отдельно не проецируется. Full text и raw response не сохраняются.

## Identity, relevance и provenance

Переиспользована ScientificIdentifierNormalizer с дополнительной positive PMID,
canonical PMCID/DOI-shape проверкой. Не используются title/fuzzy identity matching.
Нет stable IDs -> record skipped. Provider record ID не заменяет научный identifier.
Связанные stable keys образуют components; разные PMID/PMCID/DOI либо conflicting
titles внутри component -> quarantine всего ambiguous component. Ничего не merge-ится
по первому совпадению. Null metadata может enrich; конфликтующая optional metadata
очищается с conflict marker. Conflicting year очищает противоречивую full date.

Дубликаты разных sources сохраняют multiple discovery paths к одному reference.
Source ID — deterministic run-scoped hash GUID; PMID/PMCID/DOI не генерируются.
Canonical URL строится из source-reported identifiers, не из LLM URL.
Идентификаторы normalized/consistency-checked, **не независимо verified в registry**.
Existing adapter pre-dedup может скрывать часть conflicting duplicates внутри одного
ответа; observed cross-source conflicts проверяются, но абсолютной гарантии нет.

Selection требует lexical topic matches, явная population — matched text; unknown
и mixed adult-child scope исключается консервативно. Ranking: topic matches в title,
затем stable identity key. Provider response order не определяет selection.
Это **не clinical relevance/quality модель**: population mention может быть background,
а не study sample. Fewer than3 и0 sources допустимы; quota не заполняется irrelevant papers.

## Persistence и API

Миграция `20261012000308_AddQuickBoundedRetrieval` добавляет nullable retrieval JSONB
в quick_research_runs и CHECKs root coherence. Старые migrations не изменены;
Deep tables не меняются. Q0 synthetic result и Q1 retrieval разделены.
При Q1 completion `result=null`; retrieval содержит version/language/query,
selected publications, attempts, outcome, rejected count и limitations — не facts.
Final candidate buffer очищается; API не выдаёт этот buffer, owner или lease.

POST/GET/history сохраняют JWT owner policy, private/no-store и одинаковый foreign/
unknown404. Snapshot validates source/attempt/discovery membership при write/read.
SQL проверяет root identity; **embedded JSONB lineage не имеет FK**. Direct SQL может
обойти write protocol, но read integrity guard отвергает wrong-run source.

## Failure и recovery

Logical attempt сохраняется Started перед HTTP. Затем: SucceededWithResults,
SucceededZeroResults, Failed, TimedOut, Disabled или OutcomeUnknown. Только bounded
safe category, без exception text/credentials. Success0 != outage; retrieved, но
unusable/irrelevant records != zero search.
Final outcomes: Selected, ZeroResults, NoRelevantSources, Partial, Failed,
NeedsClarification. Partial допустим с0 sources; это не доказательство отсутствия
исследований. All failed -> root Failed/QuickRetrievalFailed; остальные settled
outcomes -> Completed с явным retrieval outcome, не готовым медицинским ответом.

Все writes требуют lease owner/version/expiry. Stale worker не сохраняет sources
и не fail-ит нового owner. Host cancellation оставляет Started, не provider failure.
Reclaim превращает Started в OutcomeUnknown без replay; successful slots тоже не
повторяются. Неиспользованные slots ещё могут выполняться. Это bounded-call policy,
не exactly-once external execution. Storage/lease failure не скрывается как outage.

## Configuration и безопасность

QuickResearch:Enabled=false; QuickResearch:Retrieval:Enabled=false по умолчанию.
Worker without retrieval остаётся Q0 fake. SearchTimeoutSeconds=10, допустимо1..30;
retrieval lease должен покрывать `4 * SearchTimeoutSeconds + 10`, default60s подходит.
Search deadline включает gate/HTTP/body/backoff. Для короткого bounded режима нет
heartbeat; DB stalls могут привести к expiry, но не разрешают stale writes.
Обычный .NET env override: `QuickResearch__Retrieval__Enabled` и nested timeout.
Live gate не включался. Quotas/idempotency/public admission не добавлены: public
live service этим milestone не разрешён. Health не вызывает научные providers.
Нет OpenAI key/paid calls или user DB/stand migration/restart.

## Verification и сторонняя работа

Fake HTTP проверяет actual adapters, caps, batching, zero/no EFetch и metadata.
Regression cases: RU/EN, duplicates/conflicts, populations, missing abstracts,
stable order, failure/partial/timeout/cancellation, no replay, lease loss,
wrong-run source/attempt/discovery, disabled gate и bounds.
Новые PostgreSQL cases: durable before-HTTP Started, fresh-context citations,
multi-discovery, outcome reload, reclaim/fencing, root check/read guard,
owner-filtered API persistence после пересоздания host.

Параллельные Deep/M24/audit/UI изменения сохранены вне Q1. EF generator захватил
qualitative_support из чужой незавершённой работы; он исключён из собственных Q1
artifacts. Сторонние source changes и отдельная Deep migration не изменяются.
Q1 проверяется в detached `E:\MedResearch-Q1-verify` от baseline с overlay только Q1.
Canonical OpenAPI также генерируется там. Это temporary test checkout, не новая
runtime database. Модель/CI mixed dirty tree не выдаются за проверку Q1.

Local restore/build: passed,0 warnings/errors; clean Q1 pending-model check: passed.
Финальный полный isolated backend: Domain62/Application390/Infrastructure216/
Integration67 passed,0 failed,173 Docker skips; всего735 passed/173 skipped.
Q1 добавляет62 cases:41 Application,14 Infrastructure,7 PostgreSQL/Integration.
Canonical OpenAPI tests5, SDK45, frontend lint/typecheck/production build passed.
Compose config passed; Docker info failed (dockerDesktopLinuxEngine pipe отсутствует).
Сторонние dirty Deep UI/M24 tests не исключаются из их будущей работы и не чинятся
в Q1; full regression исходного смешанного дерева не выдаётся за green Q1.
Real PostgreSQL confidence заявляется только после strict CI без required skips.

## Focused engineering prompt

```text
Continue Q1 from the existing Q0; preserve unrelated dirty work.
Reuse PubMed/Europe PMC with per-request caps; do not enter Deep stages.
Prepare one bounded RU/EN query; never guess unsupported language or scope.
Select at most five consistent-ID references and preserve each discovery.
Persist safe run-scoped outcomes under the Quick fence; no uncertain replay.
Keep zero, partial and failed distinct. Do not invent metadata or clinical claims.
Use fake HTTP/providers only; keep live retrieval off. Add no LLM/UI or framework.
Verify fresh PostgreSQL migrations, ownership/recovery and full regressions in CI.
Report actual limitations. Stop after Q1; do not start answer generation.
```

Единственная следующая задача: QUICK-Q2 — bounded source-grounded answer contract,
validator и call budget до любого live LLM использования.
