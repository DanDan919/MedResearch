# F12: validation-guided LLM repair и честная проверка реального запуска

## Baseline

Работа начата от фактического чистого `main`:

- HEAD: `ad1132935c4a492cfeb281278f672aede3fecf45`
- commit: `feat: add development Codex CLI provider`
- remote: `https://github.com/DanDan919/MedResearch.git`
- upstream: `origin/main`

F11 был проверен перед изменениями. Codex CLI оставался development/manual-only
адаптером `IStructuredLlmClient`, с read-only temp working directory, strict
schema, timeout/cancellation и удалением проектных secrets из child process.
Нормальный CI не вызывает Codex, OpenAI, PubMed или Europe PMC.

F11 не достиг `Completed`: extraction отвергла неподтверждённый excerpt, а
диагностический запуск дошёл до synthesis и отверг mixed/conflict claim без
opposing evidence. Это были реальные deterministic validation boundaries, а не
успешный report.

## Что изменено

Добавлены:

- `ValidationIssue` с machine-readable `Code`, optional `Path`, bounded
  `RepairInstruction` и disposition `Repairable`/`NonRepairable`;
- `IValidationFailure`, которым реализованы extraction, grounding, evaluation
  и synthesis validation exceptions;
- `ValidationGuidedLlmRepairService` в Application, не зависящий от Codex,
  OpenAI или конкретного HTTP provider;
- `AI:ValidationGuidedRepair:MaxSemanticRepairAttempts`, default `1`, допустимый
  диапазон `0..2`;
- logging только с stage, attempt и issue codes;
- F12 tests для first-valid, repairable-invalid -> valid, invalid replacement,
  non-repairable failure, provider failure, extraction grounding repair,
  synthesis direction repair и cross-run precondition.

## Контракт repair

Алгоритм:

```text
structured provider output
        ↓
same deterministic validator
        ├ valid → return accepted result
        └ typed repairable issue
             ↓ at most configured attempts
        same task + same trusted context + same schema
             ↓
        complete replacement
             ↓
        same validator from scratch
             ├ valid → return only replacement
             └ invalid/non-repairable → fail closed
```

Предыдущий invalid object не merge-ится и не передаётся как authoritative
scientific state. В текущей реализации repair instruction сообщает только
bounded issue code/path/instruction, поэтому модель не получает непроверенный
candidate как источник фактов.

### Extraction

`EvidenceExtractor` повторно использует тот же `EvidenceExtractionStudyContext`:
тот же `SourceMaterialId`, hash и текст snapshot, тот же study metadata и тот
же `evidence-extractor-v1` schema. `SupportingTextNotGrounded` классифицируется
как repairable. Исправленный excerpt снова проходит exact normalized containment
grounding validator. Если repair остаётся invalid, `EvidenceExtractionResult` не
возвращается и store не получает данные.

Числовая grounding защита не расширялась: presence numeric token не доказывает
его семантическую связь с effect/CI/SE. Это отдельный известный gap, который F12
не маскирует.

### Synthesis

`ResearchSynthesizer` до LLM вызова проверяет, что SynthesisContext не содержит
Evidence другого ResearchRun. Затем repair использует ровно тот же context с
current-run Evidence, evaluations и deterministic quantitative values. `MixedClaimConflict`,
`InvalidDirection`, `UnknownEvidenceReference` и model citation metadata имеют
typed issue codes. Cross-run context получает `NonRepairable` и блокируется до
provider call. Report persistence получает только результат, прошедший тот же
`ResearchReportDraftValidator`.

Planner и evaluator не получили semantic repair: их current policy остаётся
strict fail-closed, потому что для них не была доказана безопасная минимальная
repair semantics. Transport retry OpenAI также не добавлялся; semantic repair не
является сетевым retry.

## Persistence, worker и security

Repair происходит внутри stage до store call, поэтому существующие idempotency,
lease owner/version fencing и PostgreSQL recovery semantics не меняются. Crash до
accepted persistence не оставляет scientific artifact; crash после обычной
validated persistence остаётся защищённым прежними idempotency/fence правилами.

Repair service не логирует raw candidate, prompt payload, abstracts, tokens,
API keys или DB connection strings. Codex CLI ограничения F11 сохранены. Ни
`.env`, ни OpenAI/Codex credentials в репозитории не добавлялись.

## Deterministic verification

Application tests проверяют:

- valid first attempt не вызывает repair;
- repair выполняется один раз и использует тот же system prompt/schema;
- second candidate проходит validator с нуля;
- второй invalid candidate приводит к исключению после двух calls;
- non-repairable issue не retry-ится;
- provider failure не превращается в repair;
- extraction восстанавливает только grounded excerpt;
- synthesis исправляет несовместимое направление claim;
- cross-run invariant блокирует вызов LLM.

Полный PostgreSQL/Testcontainers regression остаётся обязательной проверкой в
CI; local Docker в этом окружении ранее был недоступен, поэтому локальные
PostgreSQL tests могут честно skip-аться. InMemory fallback не используется.

## Live validation status

F12 live validation должна использовать ту же bounded creatine question, что F11,
реальный Codex CLI provider, PubMed, Europe PMC и изолированный PostgreSQL с
`MEDRESEARCH_LIVE_E2E_DATABASE_ACK=isolated`. Normal CI этот тест не запускает.

Фактический live-прогон завершён как `F12 COMPLETE — live pipeline completed,
insufficient evidence`. Использован ResearchRunId
`83a9329c-4985-4406-973c-b79e7b672d1d` на свежей UTF-8 PostgreSQL базе. Реально
использованы Codex CLI (`codex-cli-default`), PubMed и Europe PMC; OpenAI API не
использовался. Сохранены один ResearchPlan, четыре LiteratureSearch, 39
discovery rows, 29 distinct Studies, SourceMaterial, extraction/evaluation
artifacts и ResearchReport. Report завершён в статусе `InsufficientEvidence` с
`NoValidatedEvidence`, без claims и claim-to-Evidence links. Это завершённый
runtime pipeline и report persistence, но не доказательство научной полноты.

Перед успешным прогоном была обнаружена и устранена только проблема harness:
временная PostgreSQL база была сначала создана с WIN1251 и отвергла Unicode
Europe PMC metadata (`22P05`); после пересоздания базы с UTF-8 прогон завершился.
Ещё более ранний запуск использовал неправильное имя connection-string
environment variable и попал в локальный порт `5432`; это также не является
дефектом scientific pipeline.

Live persistence не сохраняет отдельный repair-attempt counter, поэтому этот
прогон не позволяет честно утверждать, что semantic repair был вызван live.
Repairable/failed repair, context/schema invariants и bounded budget доказаны
детерминированными Application tests; live run дошёл до Completed report без
необходимости заявлять о live repair.

## Remaining limitations

- repair не делает validator мягче и не гарантирует научную истинность;
- numeric token grounding остаётся syntactic, а не semantic;
- нет bounded OpenAI transport retry;
- planner/evaluator semantic repair отложены;
- live Codex availability и качество ответа не являются CI guarantee;
- abstract/full-text coverage и научная полнота поиска не доказываются одним
  запуском.
