# Milestone 17 — Fixed-Effect Statistical Synthesis V1

## 1. Цель

Добавить первый узкий статистический synthesis engine поверх уже существующего M15 quantitative eligibility layer. Это не новый scientific source и не новая LLM-функция: вся математика выполняется deterministic C# кодом в Application.

## 2. Safety baseline

Стартовая точка: `main`, HEAD `517de99c2556900a46445bb5fb59dc9777659d89`, рабочее дерево clean. До изменений локально прошли restore/build/test; PostgreSQL/Testcontainers tests skipped из-за недоступного локального Docker daemon.

## 3. Методологическая проверка

Перед кодом проверены authoritative references: Cochrane Handbook по inverse-variance meta-analysis и анализу ratio measures на log scale. Вывод: OR/RR/HR нельзя усреднять как raw values; их нужно анализировать на log scale, с весами inverse variance, а затем back-transform через `exp`.

## 4. Где слой находится

Новый путь:

EvidenceCorpus -> QuantitativeEvidenceAssessor -> CompatibleEvidenceGroup -> FixedEffectQuantitativeStatisticalSynthesizer -> SynthesisContext.QuantitativeSyntheses -> narrative synthesis prompt.

Persistence не менялась: M17 результат является transient read model.

## 5. Что поддержано в V1

Поддержаны только compatible ratio measures:

- OddsRatio
- RiskRatio
- HazardRatio

Каждый вклад должен иметь:

- M15 eligibility;
- normalized log-scale effect;
- positive finite variance;
- one independent Study contribution.

Минимум независимых исследований: 2 по умолчанию.

## 6. Формула

Для группы считается:

```text
weight_i = 1 / variance_i
pooled_log_effect = sum(weight_i * log_effect_i) / sum(weight_i)
pooled_variance = 1 / sum(weight_i)
pooled_se = sqrt(pooled_variance)
CI = pooled_log_effect +/- z * pooled_se
reported_scale = exp(log_scale)
```

Default confidence level: 0.95.

## 7. Почему не persist

Результат полностью воспроизводим из уже persisted lineage: Evidence, EvidenceExtraction, SourceMaterial, Study, EvidenceEvaluation и algorithm version. Отдельная таблица нужна позже, когда появится public quantitative report/API contract или audit/export requirement.

## 8. LLM trust boundary

LLM не считает pooled estimates. Prompt теперь получает deterministic quantitative synthesis section и явно запрещает модели считать, изменять или придумывать pooled effect, confidence interval, p-value или heterogeneity statistic.

## 9. Что НЕ реализовано

Нет random-effects model, tau-squared, I-squared, Q statistic, forest plot, funnel plot, p-value pooling, semantic outcome harmonization, cohort-overlap detection, MD/SMD/correlation pooling или claims formal meta-analysis completeness.

## 10. Tests

Добавлены Application tests для:

- log-scale pooling instead of raw averaging;
- inverse-variance weights;
- configurable confidence levels 90/95/99;
- invalid confidence level rejection;
- single-study rejection;
- dependent same-study rejection;
- unsupported effect-measure family rejection;
- missing variance rejection;
- SynthesisContextBuilder production path inclusion.

Full fake pipeline test дополнен проверкой, что fake LLM получает deterministic quantitative synthesis section и что M17 synthesizer produces one pooled OR result for compatible fake evidence.

## 11. Локальная верификация

Локально после реализации:

- `dotnet build` проходит без warning/error;
- `dotnet test` проходит для non-Docker suite;
- PostgreSQL/Testcontainers tests skipped локально из-за недоступного Docker Desktop daemon;
- EF pending-model check показывает no pending model changes;
- `docker compose config` проходит;
- `docker info` локально всё ещё падает из-за отсутствующего Docker daemon pipe.

## 12. Оставшиеся риски

Fixed-effect/common-effect result не доказывает homogeneity. Без heterogeneity statistics и random-effects модели результат нельзя выдавать как полноценный systematic-review meta-analysis. Совместимость outcome/population/comparator всё ещё exact-normalized, а не semantic. Перекрытие когорт не выявляется.