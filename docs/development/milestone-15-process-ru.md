# Milestone 15 — Quantitative Evidence Eligibility

## 1. Зачем появился этот этап

В MedResearch уже есть EvidenceCorpus: он собирает Evidence, SourceMaterial, Study и оценки в одну проверенную цепочку. Но число из статьи еще не означает, что его можно складывать с другим числом. OR, RR, mean difference, SMD и correlation описывают разные статистические объекты.

## 2. Где он находится в pipeline

Новый слой находится после EvidenceCorpus и до будущей статистической модели:

EvidenceCorpus -> QuantitativeEligibility -> CompatibleEvidenceGroup -> future meta-analysis engine.

Сейчас pipeline по-прежнему завершает narrative ResearchReport. Meta-analysis не выполняется.

## 3. EffectMeasure

Система классифицирует поддержанные labels в EffectMeasureType: OddsRatio, RiskRatio, HazardRatio, RiskDifference, MeanDifference, StandardizedMeanDifference и Correlation. Unknown и Other остаются допустимыми состояниями, но не становятся автоматически пригодными для количественного синтеза.

## 4. Outcome compatibility

Outcome группируется только консервативной нормализацией текста: trim, case normalization, Unicode normalization и whitespace normalization. "Depression severity" и "depression response" не объединяются автоматически.

## 5. Что такое eligibility

Eligibility отвечает на вопрос: можно ли этот Evidence использовать как вход для будущего quantitative synthesis engine. Возможны Eligible, Ineligible и Unknown. Ineligible всегда имеет reason codes: например MissingEffectValue, MissingConfidenceLevel, InvalidNumericValue или ComparatorNotCompatible.

## 6. Reported vs Derived

Если источник сообщает OR = 1.75, это reported statistic. Если приложение считает ln(OR), это derived statistic. Эти вещи не смешиваются: исходные значения остаются в Evidence, а derived значения существуют в read model с явным StatisticOrigin.

## 7. Confidence interval / SE / variance

SE может быть source-reported или derived from confidence interval. Но CI без явно указанного confidence level не превращается в SE. Система не предполагает 95% по умолчанию.

## 8. Почему p-value недостаточно

P-value не является effect size. Он не говорит, насколько велик эффект, и не заменяет OR, RR, MD или correlation. Поэтому p-value alone не делает Evidence количественно пригодным.

## 9. Несколько Evidence из одной Study

Две находки из одной Study не считаются двумя независимыми исследованиями. CompatibleEvidenceGroup хранит EvidenceCount и UniqueStudyCount отдельно и помечает зависимость, если несколько Evidence приходят из одной Study.

## 10. CompatibleEvidenceGroup

Группа строится по outcome, population, comparator, study design и effect measure type. OR не смешивается с RR, MeanDifference не смешивается с SMD, а разные outcomes не объединяются ради красивой таблицы.

## 11. Что система пока НЕ делает

Нет pooled estimate. Нет fixed-effect или random-effects meta-analysis. Нет I², Q, tau², forest plot, funnel plot, subgroup analysis или meta-regression.

## 12. Traceability

QuantitativeAssessment сохраняет ссылку на Evidence, EvidenceExtraction, SourceMaterial и Study. Поэтому можно пройти назад к source snapshot и identifiers, из которых пришла статистика.

## 13. Tests

Тесты проверяют совместимые OR, несовместимые effect measures, p-value-only сценарий, отсутствие implicit 95%, отрицательный OR, correlation boundaries, duplicate Study dependence, deterministic ordering и source truncation как limitation metadata.

## 14. Что дальше

Следующий разумный этап — спроектировать первый статистический synthesis engine для одного узкого класса совместимых данных, например log odds ratios с известной uncertainty. Это должен быть отдельный milestone, не скрытый внутри eligibility.