# F6: Детерминированные quantitative artifacts

Дата: 2026-09-29

## 1. Цель

F6 делает результаты M17-M24 долговечными и воспроизводимыми. Это не новый статистический метод и не frontend-workspace. Формулы fixed-effect, Q/df/I-squared, REML, random-effects Wald, HKSJ и prediction interval не изменялись.

## 2. Реальный путь выполнения

`ScientificResearchStageExecutor` вызывает `SynthesisContextBuilder`. Builder строит run-scoped `EvidenceCorpus`, выбирает bounded evidence, запускает `QuantitativeEvidenceAssessor`, затем `FixedEffectQuantitativeStatisticalSynthesizer`. Последний уже вызывает REML, random-effects, HKSJ и prediction calculators. F6 передаёт готовый `QuantitativeSynthesisReadiness` в `IQuantitativeSynthesisArtifactStore`, а затем тем же deterministic result наполняет `SynthesisContext` и synthesis prompt.

LLM не вычисляет pooled effect, веса, tau-squared, HKSJ или prediction interval. Он получает только уже проверенные Application values.

## 3. Что является artifact

Один artifact соответствует одной группе `GroupKey` внутри одного `ResearchRun`. Сохраняются и `Synthesized`, и `NotSynthesizable` результаты. Поэтому `tau² = 0`, `tau² NotEstimated`, HKSJ unavailable при `k = 1`, ошибки валидации и успешные интервалы не смешиваются в один «нулевой» результат.

## 4. Схема

`quantitative_synthesis_artifacts` содержит run, group, status, algorithm version, counts, confidence level, SHA-256 fingerprint и полный JSON snapshot `QuantitativeSynthesisResult`. JSON нужен для точного сохранения nullable output-ов, enum-состояний, failure reasons и вложенных M22-M24 результатов.

`quantitative_synthesis_contribution_snapshots` содержит `ArtifactId`, `AnalysisMethod`, ordinal, `EvidenceId`, `StudyId`, `EvidenceExtractionId`, `SourceMaterialId`, analysis-scale effect/variance/SE, weight и normalized weight. Для одного artifact сохраняются и `fixed-effect`, и `random-effects` наборы весов. Числа хранятся как PostgreSQL `double precision`, потому что расчёты используют C# `double`.

## 5. Trust boundary и lineage

Перед записью store проверяет, что каждый Evidence существует, принадлежит текущему run, ссылается на тот же Study и ту же EvidenceExtraction. Extraction и SourceMaterial также проверяются по run/Study. В базе стоят FK на ResearchRun, Evidence, Study, EvidenceExtraction и SourceMaterial. Raw source text в artifact и API не попадает.

## 6. Идемпотентность

Уникальность `(ResearchRunId, GroupKey)` не позволяет создать второй artifact. Fingerprint вычисляется из канонизированного deterministic result с упорядоченными contributions и reason codes. Повтор с тем же fingerprint является no-op. Новый result с тем же ключом, но другим fingerprint отклоняется и не перезаписывает историю. Artifact и его contributions пишутся одной транзакцией.

## 7. Read API

Добавлен `GET /api/research/{researchRunId}/quantitative`. Для неизвестного run возвращается 404, для известного run без рассчитанных групп — пустой массив. Ответ содержит artifact id, persisted timestamp, fingerprint и полный provider-neutral quantitative result. Lease metadata, LLM prompt, API keys и source content не раскрываются.

## 8. Проверки

Application tests проверяют стабильность fingerprint при изменении порядка contributions и отказ на `NaN/Infinity`. PostgreSQL tests проверяют сохранение точного snapshot, fixed/random contribution rows, FK lineage, повторную запись без дублей и cross-run rejection. Full fake vertical test теперь проверяет наличие artifact, contributions и `/quantitative` рядом с `/report`.

## 9. Migration

Добавлена forward-only EF migration `AddQuantitativeSynthesisArtifacts`. Старые migrations не изменялись. `dotnet ef migrations has-pending-model-changes` должен пройти после применения новой migration. Локальная попытка EF-доступа к уже запущенному PostgreSQL на `localhost:5432` получила `28P01` из-за неверного локального пароля; это отдельная конфигурационная проблема, а не изменение схемы и не подмена Testcontainers.

## 10. Что не сделано

Нет quantitative UI, forest plot, новых effect measures, model selection, automatic meta-analysis claims, persistence of raw source text, LLM calculations, prediction/tau² confidence intervals или formula changes.

## 11. Безопасность

В snapshot нет OpenAI/NCBI credentials. API возвращает только числовые outputs и identifiers lineage. Normal tests по-прежнему не вызывают OpenAI, PubMed, Europe PMC или произвольный интернет.

## 12. Следующий шаг

Единственная рекомендация после F6: **F7 — Quantitative Results Workspace**, который сможет визуализировать уже сохранённые artifacts и lineage; новые статистические методы до этого не нужны.
