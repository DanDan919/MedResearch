# F15.1: коррекция научной границы доверия

Дата: 2026-10-08. Это corrective milestone, не новая научная функция.

## Базовое состояние и безопасность истории

Исходный аудит относится к `0de619c0db344fca162944cb68511d30403bfe69`.
По отдельному разрешению пользователя создан коммит
`360e38adeab79dbbe918867c35dc55295e05fe5a`
(`docs: record independent project audit`). В нём только `problems.md` и
`project-audit-2026-10-07-ru.md`, никаких production changes.
После него `git status --short` пуст, `git diff --check` успешен.
Это фактическая чистая baseline F15.1; исторический аудит не переписан.

Ветка `main`, upstream `origin/main`, remote
`https://github.com/DanDan919/MedResearch.git`. Reset, clean, restore, stash,
amend и force-push не применялись. Предыдущий CI `37408685280` не считается
проверкой F15.1. Итоговые commit/CI факты добавляются ниже после проверки.

## Воспроизведение до production fix

Добавлены нормальные тесты production Application-классов, не импорт внешнего
black-box harness. Первый запуск дал **12 failed, 0 passed, 0 skipped**.
Локальное доказательство: `TestResults/f15-1-red/f15-1-red.trx` (ignored artifact).

| Аудит / инвариант | Постоянный regression | Красный до исправления |
| --- | --- | --- |
| P1-A: CI от другого результата | `Verifier_DoesNotVerifyCrossBoundOrMislabelledStatistics` | да |
| P1-A: -0.73 становится +0.73 | тот же параметризованный тест | да |
| P1-A: обычное or становится OR | тот же параметризованный тест | да |
| P1-A: outcome одного результата, effect другого | тот же параметризованный тест | да |
| P1-A: число hospitals становится N | `Verifier_DoesNotBindHospitalCountToParticipantRole` | да |
| P1-A: чужой confidence level | `Verifier_DoesNotBorrowConfidenceLevelFromAnotherResult` | да |
| P1-A: чужой SE | `Verifier_DoesNotBorrowStandardErrorFromAnotherResult` | да |
| P1-B: drug A и drug B вместе | отдельный несовместимый estimand test | да |
| P1-C: отклонённый CI в raw summary/prompt | `Prompt_DoesNotCarryRejectedCiFromRawSummary` | да |
| Overlapping source matches | `Resolver_RecognizesOverlappingMatches` | да |
| Undefined direction 999/-1 | два Application enum cases | да |

Дополнительная самопроверка уже изменённого binder нашла ещё один false positive:
`Mortality was 0.55 or 0.73 (95% CI 0.55 to 0.96).`
Тест до lexical fix: **1 failed / 4 passed**, `lexical-red.trx`.
Это не замалчивается как будто первый regex сразу решил проблему.

После первого green CI независимый дополнительный тест `Overall n = 63 hospitals
were randomized` также оказался красным (**1 failed**, `explicit-n-red.trx`).
Голое n= плюс слово randomized ещё не доказывает participant-role. Второй fix
требует явную participant-role, исключает непосредственно обозначенную другую
единицу counts (hospitals/clusters/events и т.п.) и сохраняет positive controls
`Participants were randomized (n=247)` / `n=247 participants`. Совпадающее
numeric occurrence в этих двух синтаксических ролях учитывается один раз.

Не все 17 проверок внешнего harness импортированы механически. Все конкретные
P1-контрпримеры перенесены в focused tests; proof corruption, unsupported level,
enum и overlap добавлены как необходимые defense-in-depth гарантии.

## Реальное решение: не token presence

`SemanticNumericGroundingVerifier`, версия `statistical-tuple-v1`:

- выбирает ровно одно явно обозначенное measure/signed-value выражение;
- outcome должен предшествовать этому выражению в выбранном локальном контексте;
- explicit contrast clauses (`whereas`, `while`, `but`) разделяют результаты;
- несколько plausible tuples или несколько выражений в неразделённом контексте
  дают `Ambiguous`, без выбора ближайшего/первого числа;
- CI/level, SE и p/operator читаются последовательно сразу после effect через
  ограниченную грамматику statistical separators; произвольный prose останавливает
  привязку, чужие соседние статистики не подбираются;
- CI требует точную пару lower/upper; confidence level принадлежит тому же CI;
- знак является частью значения, включая ASCII minus и Unicode minus/dash;
- p сохраняет точный оператор, включая `<`, `<=`, `=`, `>` и `>=`;
- N требует participant/patient/subject/individual/adult роль, в том числе при n=,
  и явно указанную overall/enrollment/randomization/analysis область;
- hospitals, arm/control/treatment/placebo/subgroup count не становятся общим N;
- несколько разных sample scopes или результатов не разрешаются угадыванием.

`SourceAnchorResolver` по-прежнему использует `source-text-v1`: FormKC,
детерминированное whitespace collapse и lowercase. Канонические offsets и SHA-256
span hash не изменены. Проверка второго вхождения начинается с `first + 1`, поэтому
`aaaa` / `aaa` неоднозначно. Supporting text ограничен прежними bounds.

Добавлено nullable `LexicalText` в JSON proof: case-preserving projection exact
SourceMaterial, не регистра model-supplied quotation. Оба варианта нормализации
используют одинаковый посимвольный проход. Domain требует lowercase lexical text
равным canonical Text, downstream повторное разрешение проверяет исходный регистр.
Только source-reported uppercase `OR` подтверждает bare abbreviation; обычное
lowercase `or` не проходит. Полная фраза `odds ratio` поддерживается независимо
от регистра. Старый anchor без lexical proof не подтверждает bare OR, не обновляется
автоматически и не переписывается в БД.

### Точное значение Verified

`Verified` означает однозначную детерминированную роль/отношение **в рамках этой
ограниченной грамматики**, плюс exact membership на downstream границе. Это не
доказательство истинности публикации, причинности, клинической эквивалентности,
валидированного risk of bias или универсальной NLP semantic entailment.
Сложные таблицы, adjusted/unadjusted результаты, ковариаты, множественные группы,
неявный timepoint и перефразирование могут дать false negative. Предпочтена
точность, не максимальная полнота. Общая семантика outcome/population не решена.

## Estimand и quantitative input

`QuantitativeEvidenceAssessor` имеет версию
`quantitative-eligibility-v2-bound-estimand`.
Nullable `Evidence.Timepoint` хранит только явно сообщённую outcome timepoint
фразу (до 100 символов), не значение из ResearchPlan и не guessed follow-up.
Timepoint proof допускает `at`, `after`, labelled follow-up, не `for` duration.

Группа строится из нормализованных:
outcome / population / comparator / design / measure / intervention / timepoint.
Ключ имеет prefix `estimand-v2|` и length-prefixed components: пользовательский
delimiter внутри label не объединяет группы. Нормализация точная FormKC,
whitespace, lowercase, без клинических synonyms и конвертации единиц времени.

Intervention и comparator требуют явной направленной пары `versus`, `vs`,
`compared with/to`, `against`; перестановка ролей не поддерживает исходный estimand.
Missing intervention или timepoint всегда ineligible, даже если отсутствуют у
обоих исследований. Разные drug A/B или timepoints не pool; разные comparator
также отдельны. Положительный одинаковый контекст создаёт ready group, порядок
Evidence не меняет membership/key. Два зависимых Evidence одного Study по-прежнему
не становятся двумя независимыми contributions.

M17-M24 calculators, формулы, reference assertions не изменены. Изменяется только
допустимое множество contributions и версия grouping identity. Старые positive
fixtures, которые пользовались отсутствующим proof или Verified с null anchor,
заменены явными source/anchor/context fixtures с теми же численными значениями;
ослабления numerical assertions нет. Historical artifacts остаются immutable
historical snapshots, не становятся автоматически результатами нового validator.

## Corpus и proof gate

`EvidenceNumericProof` повторно проверяет:
exact SourceMaterialId / Study / completed extraction / current run / scope;
known normalization; content hash; уникальное source membership;
offsets / span hash / lexical case; current tuple relation.
Исходные persisted mandatory facts должны существовать ровно один раз и быть
Verified с тем же anchor. Null/empty/duplicate/unverified proof не повышается.
Самосогласованного hash недостаточно: anchor должен существовать в exact Content.
Неизвестная версия, offsets вне источника и подменённый lexical case блокируются.
Confidence level без Verified proof не участвует в CI-derived SE/variance.

Domain отвергает undefined EvidenceDirection и Verified без valid anchor.
Это не SQL trigger, не заявленная возможность публичной HTTP инъекции.
Тесты повреждённых Application snapshots моделируют внутренний риск.

Infrastructure загружает Content только для exact extraction source IDs во
внутренний `[JsonIgnore]` snapshot. Public provenance API не получает Content;
same-run citation/owner boundaries сохранены. Поля bounded numeric proof в public
DTO в этом milestone **не добавлены**; отображение anchor/proof в UI отложено.

## Narrative containment

Raw model `ResultSummary` остаётся persisted extraction description/provenance,
но не авторитетным numeric input. Он исключён из evaluation и synthesis prompts.
`SynthesisEvidenceProjection` заменяет его безопасным служебным описанием и
обнуляет неподтверждённые structured statistics. Корректные подтверждённые значения
и supporting quotation остаются. Qualitative Evidence не уничтожается за отсутствие
quantitative context. Это не regex-redaction arbitrary свободного текста.

В процессе самопроверки обнаружен дополнительный обход: ContextBuilder читал raw
snapshot Evidence, а не повторно проверенный corpus. Он закрыт; постоянный тест
`ContextBuilder_UsesRevalidatedProjectionNotRawSnapshot` проверяет именно эту цепочку.
Capturing fake `IStructuredLlmClient` проверяет **реальный запрос** из
`ResearchSynthesizer`, а не только отдельную helper-строку: 999.123 и неверные
CI 0.1234/0.2345 отсутствуют, подтверждённый effect 0.73 сохраняется.

Source quotation явно помечена как quote, не дополнительное permission для
numeric claims. Она может содержать другие реальные source numbers. Report draft
validator пока не доказывает каждую числовую фразу final free text. Нельзя говорить,
что теперь невозможна вся narrative hallucination. Закрыт конкретный bypass raw
ResultSummary и неподтверждённых structured fields, не общая генерация prose.

Prompt versions: extractor `evidence-extractor-v3-bound-tuples`, evaluator
`evidence-evaluator-v2-no-raw-summary`, synthesizer
`research-synthesizer-v2-trusted-evidence`.

## Repair, cancellation, concurrency

F12 bounded semantic repair не переписан: default 1, конфигурация 0-2, только typed
repairable failures, тот же источник/schema/validator, rejected candidate не
сохраняется. Unsupported optional statistic опускается, а не провоцирует модель
придумать данные. Transport retries не являются semantic repair.
Cancellation/provider/cross-run/infrastructure errors не разрешаются этим repair.
Existing fake repair и same-source tests сохранены.

Worker owner/version fencing, heartbeat, claim/reclaim SQL, terminal lease cleanup,
source/run FK и auth ownership не изменены. Существующие stale-writer/cross-run
PostgreSQL regressions обязательны в CI. Новые direct-corpus тесты дополнительно
не допускают другую run/Study/skipped extraction и несовместимый source scope.

## Persistence и migration

Новая forward migration `20261008041640_AddEvidenceTimepoint`: единственная новая
колонка `evidence.timepoint`, nullable `varchar(100)`. Новых indexes нет, старые
миграции не редактировались. LexicalText хранится в существующем grounding JSON,
отдельного schema изменения не требует. Старые записи имеют null timepoint и
не могут wildcard-match новый quantitative estimand.

Новый PostgreSQL test
`PersistExtractionResultAsync_FreshReadRechecksTupleProofAndTimepoint` сохраняет
timepoint/proof, намеренно повреждает accepted snapshot CI, читает через fresh
DbContext и проверяет, что corpus/assessor/context/prompt не доверяют stale Verified.
Fresh migration test требует новую миграцию в applied history.

## Локальная верификация

| Набор | Passed | Failed | Skipped | Условия |
| --- | ---: | ---: | ---: | --- |
| Domain Debug / Release | 32 / 32 | 0 / 0 | 0 / 0 | полный набор |
| Application Debug / Release | 238 / 238 | 0 / 0 | 0 / 0 | полный набор, adversarial + M17-M24 |
| Infrastructure Debug / Release | 73 / 73 | 0 / 0 | 0 / 0 | fake HTTP/providers |
| Integration Debug / Release | 26 / 26 | 0 / 0 | 81 / 81 | Docker unavailable |
| API frontend | 14 | 0 | 0 | Vitest |
| Web frontend | 22 | 0 | 0 | Vitest |
| Playwright Chromium | 11 | 0 | 0 | deterministic local/browser fixtures |

Итого backend на одну configuration: **369 passed, 0 failed, 81 skipped**.
Локальный SDK: `10.0.401`. Debug и Release restore/build успешны, 0 warnings/errors. EF pending-model check
успешен, Compose config успешен, diff check успешен. `docker info` падает из-за
отсутствующего `dockerDesktopLinuxEngine` pipe. Это не PostgreSQL runtime proof.
Docker Desktop не ремонтировался.

Frontend: frozen install, api generation (без diff публичного client), lint,
typecheck, unit tests, Next production build, Playwright успешны.
Hydration/runtime-error regression зелёный. UI/desktop packages не содержат unit
tests и завершаются через passWithNoTests; их нельзя считать протестированными
native apps. Native Tauri build не выполнялся и не входит в F15.1.

Live test projects отдельно при выключенных flags: LiveE2E 3 skipped,
LiveEuropePmcFullText 1 skipped, LiveEuropePmc 1 skipped, LivePubMed 1 skipped.
Это ожидаемые opt-in skips вне normal solution/CI, не выполненные live проверки.

## Live scientific workflow

**NOT RUN**: локальный PostgreSQL/Docker недоступен. ResearchRunId отсутствует;
live Verified/Ambiguous/Unsupported counts, groups и ручная выборка пяти фактов
не получены. Никаких результатов не фабриковали. Никакой OpenAI key или paid API
не использовался. Live Codex/OpenAI/PubMed/Europe PMC вызовов не было.

## CI и git

Workflow `.github/workflows/ci.yml` не изменён: ubuntu-latest, .NET 10.0.x,
Node 24.x/pnpm 11.19, Docker/Testcontainers PostgreSQL; strict
`MEDRESEARCH_REQUIRE_DOCKER_TESTS=true` и TRX skip gate.
Normal CI не вызывает live научные сервисы. Build/frontend/browser/EF/Compose,
fresh migrations, recovery/fencing/auth/provenance/repair должны пройти.

Первый feature commit: `3d9dc999f27d3275d774cd265367b3f886a5a654`,
`fix: enforce bound scientific evidence and estimand compatibility`.
Push в origin/main успешен. Первый реальный CI
[37729129674](https://github.com/DanDan919/MedResearch/actions/runs/37729129674)
завершился success. TRX annotations: Domain 32, Application 233, Infrastructure
73, Integration 107; все executed/passed, failed=0, skipped=0. Всего 445.
Docker info, fresh migration/runtime suite, EF pending model, Compose, все frontend
steps и Playwright success. 81 тест, пропускаемый локально из-за Docker, выполнен
в составе 107 Integration tests. GitHub Node20-action warning не блокировал run.

Первый green run **не объявлен окончательным**: после него найден и исправлен
explicit hospital n= контрпример. Final corrected commit/run и его counters
добавляются после следующей независимой проверки, не экстраполируются из первого CI.

Перед feature commit reviewed staged scope/diff, whitespace check и search по
api_key/ApiKey/OPENAI_API_KEY/Authorization/Bearer/secret/password; credentials
не добавлены. Нет provider, auth, frontend или M17-M24 calculator changes.

## Остаточные риски и следующий milestone

Не заявляется универсальный semantic parser: сложные outcome/population/subgroup/
adjustment отношения требуют дальнейших adversarial cases. Conservative matcher
может исключать реальные результаты. Free-text final report entailment остаётся
отдельным scientific-integrity ограничением. Старые persisted artifacts не
пересчитаны; raw descriptions в read API остаются untrusted extraction text.

Не затронуты audited P2: durable failed-provider-attempt provenance, global
SourceMaterial attribution, Europe PMC publication/index-date semantics, bounded
HTTP-body/deadline handling, mobile width, dependency advisories, native desktop,
production browser auth flow, OpenAPI drift. Их нельзя объявлять исправленными.

После независимого green CI и закрытия конкретных P1 единственный рекомендуемый
следующий milestone: **F15.2 — Provider & Runtime Integrity Hardening** (durable
provider attempts, publication/index dates, bounded HTTP body/deadlines). Он здесь
не начат. Если CI выявит оставшийся P1, F15.1 остаётся незавершённым до исправления.
