# F7: Quantitative Results Workspace

## 1. Цель

F7 добавляет frontend-панель для просмотра уже сохранённых F6 quantitative artifacts. Панель является инструментом чтения и навигации по научному результату. Она не является статистическим движком.

## 2. Фактический источник данных

Страница `/research/{researchRunId}/quantitative` вызывает `GET /api/research/{researchRunId}/quantitative`. Запрос кэшируется TanStack Query по ключу `research -> {researchRunId} -> quantitative`, поэтому артефакт одного ResearchRun не может попасть в другой через общий cache key.

Ответ возвращает массив артефактов. Один артефакт соответствует одной `GroupKey`. Если групп несколько, UI показывает selector и сохраняет каждую группу отдельно. Если массив пуст, показывается состояние `No quantitative artifact`, а не выдуманный нулевой результат.

## 3. Что реально доступно

Проверенные поля: effect measure enum, group compatibility keys, status, method, algorithm versions, confidence level, evidence/study counts, analysis-scale и reported-scale pooled values, Q/df/I², tau², random-effects Wald, HKSJ, prediction interval, fixed/random contribution snapshots и четыре lineage ID на contribution.

`I²` хранится как доля от 0 до 1 и только форматируется для экрана как процент. Остальные числа форматируются для чтения, но не меняются в данных и geometry plot.

Не доступны в F6 artifact: study title, PMID/PMCID/DOI, author metadata и study-level confidence intervals. Поэтому contribution table показывает устойчивые Evidence/Study IDs и lineage IDs. UI не делает запрос на каждую contribution, не раскрывает SourceMaterial и не строит CI из SE.

## 4. Структура workspace

- Header: effect measure, group keys, counts, confidence level, artifact timestamp.
- Summary effects: Common/Fixed и Random Effects рядом, без выбора «лучшей» модели.
- Forest plot: SVG с contribution point estimates и сохранёнными pooled intervals на analysis scale. Study-level CI отсутствуют и явно помечены как недоступные.
- Heterogeneity: Q, df, I² и tau². `tau² = 0` показывается как ноль, `null` или `NotEstimated` не превращается в ноль.
- Inference: Wald и HKSJ intervals рядом с одним shared random-effects point estimate.
- Prediction interval: отдельная секция, не смешанная с confidence intervals.
- Contributions: точные persisted effect/SE/weights и раскрываемая lineage-цепочка ID.
- Reproducibility: artifact ID, fingerprint и algorithm versions.

## 5. Научная граница

Frontend не вычисляет effect sizes, log/exp transforms, SE, variance, pooled results, weights, Q, df, I², tau², Wald, HKSJ, prediction intervals, p-values, certainty или recommendations. SVG coordinates являются только presentation mapping уже имеющихся analysis-scale values.

Общие/Random Effects не ранжируются. HKSJ не показывается как другой pooled effect. Prediction interval объясняется как диапазон для нового сопоставимого исследования в fitted random-effects model, а не как прогноз пациента. UI не использует traffic-light significance и не даёт клинических рекомендаций.

## 6. Контракт и runtime validation

OpenAPI schema была расширена существующими вложенными F6 структурами: heterogeneity, REML, random-effects, HKSJ и prediction. TypeScript regenerated через `pnpm api:generate`. Zod проверяет nullable fields, numeric enums, contributions и все вложенные output states до передачи в UI.

API errors разделяются на unknown run (`404`) и прочие ошибки загрузки. Известный run без artifact получает пустой массив и отдельное explanatory state.

## 7. Навигация и печать

Из report workspace ссылка `Quantitative results` появляется только после успешного bounded quantitative request с непустым массивом. Quantitative page сохраняет run context и имеет back link. Print controls и navigation скрываются при печати; table и textual values остаются доступными.

## 8. Проверки

Frontend unit tests проверяют summary, HKSJ/Wald separation, prediction interval wording, tau² zero, multi-group state, no-artifact state, 60-contribution fixture и accessible plot. API tests проверяют nested contract и run-safe query keys. Playwright покрывает deep link, desktop/mobile workspace, forest plot и отсутствие artifact.

## 9. Ограничения и следующий шаг

F7 не добавляет Study metadata projection, потому что F6 quantitative endpoint её не сохраняет внутри snapshot, а текущая задача не требует расширять backend за пределы truthful read model. Для полноценного human-readable publication label потребуется отдельное, явно спроектированное read-model изменение.

Рекомендуемый следующий milestone: **F8 — Evidence & Provenance Explorer**. В рамках F7 он не начинается.
