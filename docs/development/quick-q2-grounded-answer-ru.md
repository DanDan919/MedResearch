# QUICK-Q2 — source-linked medical answer foundation

Дата: 2026-10-12. Starting HEAD: `abaf14f9f491ed11d68483fa723fe465239a8aa4`.
Ветка main; remote `https://github.com/DanDan919/MedResearch.git`.
Q2 не включает live generation, deployment или Q3 UI.

## Архитектура и фактический вызов

`POST -> QuickResearchService -> QuickResearchRun -> BackgroundQuickResearchWorker`

`QuickResearchProcessor -> Q1 QuickRetrievalProcessor -> saved final retrieval`

`QuickAnswerPrompt -> fenced ReserveAnswerAsync -> IStructuredLlmClient.GenerateStructuredAsync<QuickAnswerDraft>`

`QuickAnswerValidator -> fenced SettleAnswerAsync -> owner-scoped GET`

Q1 сохраняет final подборку, но при включённом Q2 ещё не завершает весь run.
Q2 получает только эту persisted snapshot, не глобальные Study/SourceMaterial или
чужие runs. После crash с final retrieval поиск не повторяется. Исторические
Completed Q0/Q1 runs не переоткрываются. Default Q0/Q1 пути остаются прежними.
Нет Planning/Extraction/Evaluation/ResearchSynthesizer/quantitative calls, агентов,
нового scheduler, billing или accounting framework.

Основные новые файлы: Domain `QuickAnswer.cs`; Application `QuickAnswerContracts`,
`QuickAnswerPrompt`, `QuickAnswerValidator`, `QuickAnswerProcessor`; три test files.
Изменены Quick processor/store/DI/config/API DTO, optional structured-request
limits, ограниченный transport branch, forward migration и generated contracts.
Deep scientific tables/algorithms не изменены в Q2.

## Typed answer и API

Существующий GET `/api/quick-research/{id}` добавляет nullable `answer` и
`answerStatus`. Root status остаётся Queued/Processing/Completed/Failed. `retrieval`
по-прежнему содержит selected publications, authoritative source-reported metadata
и search provenance; Q1 result не становится Deep Evidence. Q2 `result=null`.
History owner-only summaries остаются прежними. Нет browser UI/BFF expansion.

Answer shape (обозначения, не реальный медицинский результат):

```json
{
  "quickRunId": "<current-run-uuid>",
  "shortAnswer": "<exact text of first validated finding>",
  "findings": [{
    "text": "<plain non-numeric source-linked statement>",
    "citations": [{"sourceId": "<selected-source-uuid>", "excerpt": "<exact abstract excerpt>"}]
  }],
  "contradictions": [],
  "limitations": ["<bounded limitations plus backend safety statements>"],
  "uncertainty": "VeryLimited",
  "sourceReferences": ["<distinct cited source uuid>"]
}
```

Findings0..6, contradictions0..3, draft limitations1..4 (final <=8), sources<=5.
Finding text/summary<=600, limitation<=500, excerpt20..800 characters.
Findings требуют1..5 distinct citations; contradiction требует2..5 разных sources.
sourceReferences точно равен union всех cited IDs. ShortAnswer равен первому
finding: независимый неподкреплённый summary не принимается. Empty contradiction
array валиден; фиксированного числа findings нет.

Uncertainty: Limited/VeryLimited/Insufficient — это категории контрактного output,
не GRADE и не измеренная вероятность правильности. Insufficient имеет0 findings,
contradictions и references и только фиксированный backend summary на RU/EN.
При отсутствии пригодных abstracts такой answer создаётся **без LLM**. Полный
provider failure остаётся Failed/RetrievalFailed, не «научных доказательств нет».
Partial coverage получает обязательное предупреждение от backend.

GET не выдаёт generation record, prompt, fingerprint, token budget, provider/model
metadata, raw response или exception text. SafeError: QuickAnswerFailed,
QuickAnswerOutcomeUnknown либо сохранённый QuickRetrievalFailed. Private/no-store,
JWT owner, foreign/unknown404 остаются прежними. OpenAPI/TS реально regenerated.

## Citation / source guardrails

Только IDs current QuickRun и selected Q1 sources. Citation к metadata-only source
отвергается: title не является clinical support. Source, исключённый из bounded
prompt, не может быть принят в draft, даже если был в общей Q1 подборке.
При write/read повторно проверяется root/source membership и actual abstract.

`quick-excerpt-v1`: Unicode NFC + collapse whitespace, case-sensitive ordinal
substring. Никакого lowercase, снятия punctuation/negation, fuzzy matching или
поиска числовых tokens по всему корпусу. Quote обязан иметь **один** exact match
в своём abstract; повторный equally matching span отвергается. Выдержки сохраняются,
но не превращаются в Deep grounded numeric Evidence.

Q2 консервативно запрещает цифры, numeric symbols, обычные RU/EN quantity words и
очевидные percentages/doses/comparisons в model prose. Числа допустимы только
в exact excerpts. Это строже, чем проверка отдельных статистических значений:
numeric result binding/normalization Q2 не реализует. Guard может отвергать
безопасные обозначения/формулировки; он не является полноценным NLP detector.
Known prompt-instruction, individual-treatment и review-completeness patterns
отвергаются; список не доказывает обнаружение всех обходов.

Question/publication text JSON-encoded как untrusted data. System prompt запрещает
выполнять embedded commands, использовать outside knowledge, диагнозы/назначения,
выдумывать disagreement/statistics или выдавать подборку за exhaustive review.
Backend добавляет обязательные limitations и проверяет их при последующем чтении.
Это защита структурной границы, **не доказательство отсутствия prompt injection**.

## Бюджет и transport

Normal path: ровно **одна зарезервированная generation**, no semantic repair.
AttemptCount0 означает no-dispatch insufficiency/disabled/input failure;1 означает
израсходованный reservation budget, не доказательство одного оплаченного ответа.
Count>1 недопустим. Не вводится config, увеличивающий число попыток.

QuickResearch:Answer defaults:

| Setting | Default | Validation |
| --- | --- | --- |
| Enabled | false | live не включался |
| TimeoutSeconds |20|1..60|
| MaxSourceCharacters |6000|100..6000|
| MaxPromptCharacters |40000|2000..40000|
| MaxOutputTokens |1200|256..2000|
| MaxResponseBytes |64000|4096..64000|

До5 sources, только title/ID/abstract; authors/PMID/DOI/foreign data не передаются
LLM для самостоятельного сочинения библиографии. Oversized/null abstracts исключены
из prompt, а не незаметно обрезаны. Total character cap включает system/user/schema.
Prompt содержит language, retrieval outcome, omissions и coverage limitations.
SHA256 fingerprint связывает exact prompt/schema/version/bounds без сохранения prompt.

`StructuredLlmRequest.Limits` необязателен. Deep requests без него сохраняют старые
token/default limits. OpenAI limited path использует минимум request/global token
cap, ResponseHeadersRead, existing BoundedProviderBody и linked deadline; headers
и body имеют deadline, decoded body имеет byte cap, automatic retries отсутствуют.
Malformed/unknown output fields и oversize не становятся trusted answers. Domain/
Application checks выполняются независимо от provider strict schema.
Codex CLI rejects limited requests **до процесса**: текущий adapter не гарантирует
token cap. Тесты используют deterministic client/fake HTTP, не CLI.

Env overrides — обычные `QuickResearch__Answer__...`; нет нового секрета/API key.
Enabled Q2 требует enabled Q1 и lease >=4*search timeout + answer timeout +10s.
С default10s/20s это минимум70s; Q0/Q1 default lease60s не менялся. Fake API tests
используют90s. Нет heartbeat для этого bounded path. DB stalls могут потерять lease,
но не снять fence или разрешить новый generation call. Settings не являются
публичными admission/quota/dollar controls; deployment/live не разрешён этим Q2.

## Persistence / recovery

Forward migration: `20261012112257_AddQuickGroundedAnswers`.
Одна nullable `answer_generation JSONB` в существующей quick_research_runs.
Record: ID/root/version/fingerprint/AttemptCount/timestamps/outcome/safe category/
validated Answer. Internal persisted enum values0..4 зафиксированы явно.
CHECK связывает root, settled retrieval, processing/terminal status и answer shape;
embedded source references не имеют SQL FK. Write/read application guards обязательны.
Новых Deep columns, indexes, tables, databases или переписанных migrations нет.

Reserve — один atomic UPDATE с generation IS NULL, Processing, owner/version/expiry,
exact question и JSONB equality с saved retrieval. Транзакция не удерживается через LLM.
Два executors не могут оба зарезервировать попытку. Final retrieval больше не
переписывается через обычный Quick write protocol.
Settlement — один fenced UPDATE; matching generation ID/fingerprint/pending state,
exact saved inputs, answer и terminal cleanup commit-ятся вместе.

Crash **до** reservation: resume saved retrieval и reserve допустим.
Crash **после** reservation, даже до HTTP dispatch: budget не возвращается;
Started при reclaim -> OutcomeUnknown/Failed без LLM. Host cancellation оставляет
pending record и отдаёт cancellation hosting; timeout -> terminal OutcomeUnknown.
Crash после ответа, но до settlement: ответ не подтверждён/не сохранён, recovery
также unknown, не новая generation. Stale receipt/failure не перезаписывает нового
owner. DB failure не маскируется как successful/medical result; pending budget
остаётся recoverable. Retrieval при generation failure сохраняется и не повторяется.
Terminal runs не reclaim-ятся, lease metadata очищается. Нет retry endpoint/Q3.

## Проверки и dirty-tree handling

Новые deterministic cases: cited answer/disagreement,0 findings/insufficiency,
metadata-only/omitted sources, foreign/invented IDs, absent citations, unsupported
summary, normalization/ambiguous quote, untrusted instructions, numeric/obvious
treatment/completeness prose, malformed/oversize HTTP/draft, timeout/cancellation,
single reservation/duplicate executor, crash-before/after receipt, no redispatch,
saved retrieval resume, shared adapter bounds и invalid config.
Real PostgreSQL cases: durable pre-dispatch state, fresh-context source/answer,
concurrent reservation, expiry/fencing, drift receipt rejection, zero/failed/unknown
states, DB root check/read guard, owner-filtered GET/history и API host restart.

Параллельные Deep/M24/UI/audit changes сохранены. Implementation/model/OpenAPI/tests
проверяются в detached `E:\MedResearch-Q2-verify` от actual baseline. Main EF snapshot
получает только Quick addition; staged snapshot содержит только Q2, не стороннюю
qualitative_support migration. Dirty ARCHITECTURE/Deep docs не переписываются;
архитектура Q2 описана здесь и связана из README/current-state. Исходное mixed dirty
дерево не выдаётся за протестированный Q2 commit.

Локально: restore/build, pending-model, OpenAPI drift5, SDK45/Web157,
lint/typecheck/production build, Compose config passed. Docker info failed:
dockerDesktopLinuxEngine pipe unavailable. Mandatory PostgreSQL runtime verification
требует green strict CI; local skips не подтверждают SQL/concurrency guarantees.
Финальный local backend: Domain62/Application437/Infrastructure234/Integration67
passed,0 failed,182 Docker skips; всего800 passed/182 skipped. Q2 добавляет74 cases:
47 Application,18 Infrastructure,9 PostgreSQL/Integration. После EF generation
Windows escaped CRLF churn в двух старых constraint strings был исключён только
из новых generated artifacts; old migrations не изменены. Pending model passed.
CI результат фиксируется после фактического PostgreSQL прогона.

## Что не доказано

Citation existence/unique quote доказывают наличие текста и lineage, не entailment,
population/timepoint applicability, causal interpretation, source quality или
медицинскую правильность model prose. Agreement/contradiction semantics и plain-
language readability не проверены экспертами. Regex guardrails неполны и могут
быть чрезмерно консервативными; нужны adversarial/scientific review и оценка ответов.
Короткие/oversized/missing abstracts ограничивают coverage. Q1 lexical selection и
RU glossary остаются прежними ограничениями. Live availability/latency/cost не
проверены, provider/model metadata не сохраняется для воспроизводимости модели.
Нет spend/admission safeguards для публичного live сервиса и нет exactly-once billing.
Privileged SQL, удаление ledger или смена контрактов вне application protocol не
являются безопасным способом разрешить повторную попытку.

Единственная рекомендуемая следующая задача: QUICK-Q3 — owner-scoped read-only
интерфейс Quick с явными source links, uncertainty/insufficiency и safe errors,
без включения live providers или автоматических повторов generation.
