# F11: Codex CLI и первый реальный research run

## Зачем нужен F11

До F11 автоматические проверки использовали deterministic fake LLM, а рабочий
OpenAI adapter требовал API key. Цель F11 — проверить уже существующие
planner/extractor/evaluator/synthesizer на реальном structured model provider,
не меняя научные контракты и не превращая Codex CLI в production dependency.

## Фактический baseline

- Commit до F11: `b41b642939c552679578dc6ec6a69482c7be7a5d`.
- F10 authentication/ownership boundary присутствует и проверен.
- Рабочее дерево было чистым, ветка `main` отслеживала `origin/main`.
- Codex CLI: `0.160.0`; доступны `codex exec`, stdin, `--output-schema`,
  `--output-last-message`, `--sandbox read-only`, `--ephemeral`.
- Минимальный structured smoke успешно вернул `{"status":"ok"}` без
  `OPENAI_API_KEY`. Токены и credential store не читались.

## Архитектура provider

Application видит только `IStructuredLlmClient`. Infrastructure выбирает
`OpenAIStructuredLlmClient`, `CodexCliStructuredLlmClient` или тестовый fake.
Codex provider получает system/user prompt через stdin, записывает ровно ту же
effective JSON Schema во временный файл, читает только final output file после
нулевого exit code и десериализует его в тот же CLR type. Валидация ResearchPlan,
Evidence, Evaluation и ResearchReport остаётся в Application.

`AI:Provider=CodexCli` разрешён только для `Development` и
`ManualScientificE2E`; production selection отклоняется. Рабочая директория
каждого вызова создаётся в temp вне репозитория, sandbox фиксирован как
`read-only`, `--search` не включается, а аргументы передаются через
`ProcessStartInfo.ArgumentList`. Prompt не попадает в shell command string и не
обрезается молча. Timeout/cancellation завершают только созданное process tree.

Codex CLI — локальный процесс, но не обязательно локальная модель: inference
может выполняться удалённо через ChatGPT/Codex account. MedResearch не читает и
не сохраняет credentials.

## Live run status

Docker Desktop Linux engine оставался недоступен, поэтому для ручной проверки
был создан отдельный временный PostgreSQL cluster на `127.0.0.1:55432` с
изолированными базами. Это не изменяло системный PostgreSQL service и не
заменяло PostgreSQL на SQLite.

Фактические проверки:

| Проверка | Результат |
|---|---|
| `codex exec` structured probe | PASS, `{"status":"ok"}` |
| Adapter smoke через `CodexCliStructuredLlmClient` | PASS, 1 test |
| Planner role через existing prompt/schema/validator | PASS, 1 test |
| Full run attempt 1 | `Failed` на Extracting: supporting text не найден в source abstract |
| Full run diagnostic retry | Дошёл до Synthesizing; `ResearchSynthesisValidationException` отверг invalid mixed/conflict claim |
| Fresh PostgreSQL migrations | PASS, все migrations применились в пустой базе |
| Full Level C final status | NOT COMPLETED; report не persisted |

Последний диагностический run имел фактические переходы
`Queued → Planning → Searching → Extracting → Evaluating → Synthesizing → Failed`.
Реальные PubMed ESearch/EFetch и Europe PMC REST/full-text requests выполнялись;
Codex CLI прошёл planner, extractor, evaluator и synthesizer calls. Отказ на
синтезе был fail-closed результатом существующего scientific validator, а не
подменой фейком или ослаблением проверки.

Во время свежей миграции обнаружился и исправлен порядок hosted services:
startup migrations теперь регистрируются до background worker, чтобы worker не
начинал polling пустую схему. Europe PMC у одной full-text записи вернул XML с
DTD, который текущий безопасный parser отверг; acquisition продолжил run с
bounded unavailable-material semantics.

Таблица фактического результата:

| Поле | Значение |
|---|---|
| ResearchRunId | `80f28300-dd4e-4620-a9ee-50c286e51efa` в diagnostic run |
| Question | creatine supplementation vs placebo in healthy adults |
| Providers | `codex-cli`, PubMed, EuropePmc, Europe PMC full text |
| Stage transitions | Planning, Searching, Extracting, Evaluating, Synthesizing |
| Evidence/SourceMaterial | 3 grounded Evidence, abstract SourceMaterial |
| Quantitative artifact | не достигнут; не было достаточного deterministic quantitative input |
| Report | не persisted из-за synthesis validation failure |
| Terminal status | `Failed` |

Статус F11: **COMPLETE — provider integration; PARTIAL — full live research
run**. Полный real run воспроизводимо стартует на PostgreSQL и доходит до
научных validation boundaries, но один успешный report с live Codex не заявляется.

## Ограничения доверия

Strict JSON Schema не устраняет hallucination. Источником научного текста
остаются PubMed/Europe PMC SourceMaterial, grounding validation остаётся в
Application, а численные pooled estimates вычисляются deterministic C# кодом.
Один реальный запуск не доказывает клиническую корректность, полноту поиска,
отсутствие hallucinations или production readiness.

## Воспроизведение

1. Убедиться, что CLI залогинен через обычный Codex механизм.
2. Запустить `MEDRESEARCH_RUN_LIVE_CODEX_CLI=true dotnet test
   tests/MedResearch.LiveE2EValidationTests/MedResearch.LiveE2EValidationTests.csproj
   --filter FullyQualifiedName~CodexCliLiveSmokeTests`.
3. Для planner contract добавить `MEDRESEARCH_RUN_LIVE_CODEX_ROLES=true`.
4. Для полного E2E использовать изолированную PostgreSQL, установить
   `MEDRESEARCH_LLM_PROVIDER=CodexCli`, `MEDRESEARCH_RUN_LIVE_E2E=true`,
   `MEDRESEARCH_RUN_LIVE_CODEX_CLI=true` и
   `MEDRESEARCH_LIVE_E2E_DATABASE_ACK=isolated`. Connection string и bounded
   limits задаются обычными environment variables, например
   `ConnectionStrings__MedResearch`, `Database__ApplyMigrationsOnStartup=true`,
   `SourceAcquisition__MaxStudiesPerRun=1`,
   `EvidenceExtraction__MaxStudiesPerRun=1`,
   `EvidenceEvaluation__MaxStudiesPerRun=1`, `Synthesis__MaxClaims=1`.

Live harness intentionally reads configuration from the process environment;
normal CI does not execute it and never needs Codex, OpenAI, PubMed, or Europe
PMC credentials.

Обычные `dotnet test` и CI Codex не запускают.
