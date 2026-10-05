# F15: source-anchored semantic numeric grounding

## Цель и исходная проверка

F15 продолжает состояние F14 на `dceea9a483cb5b5dd483ef7dfacd835d4f02aad7`.
До изменения код проверял два более слабых свойства: `supportingText` был
подстрокой выбранного `SourceMaterial`, а числовой токен встречался где-то в
этом тексте. Это не связывало `0.73`, CI и p-value с одним outcome,
population, subgroup или timepoint. Старый `EvidenceNumericGroundingValidator`
сохранён как совместимый низкоуровневый helper и тестовый исторический слой,
но production-путь F15 использует новую source-anchored проверку.

## Фактический поток

```text
SourceMaterial.Content
  -> SourceAnchorResolver (source-text-v1, unique canonical span)
  -> SemanticNumericGroundingVerifier (local statistical context)
  -> EvidenceExtractionDraftValidator
  -> Evidence.NumericGrounding + PValueOperator
  -> EfEvidenceExtractionStore / JSONB
  -> EvidenceCorpusBuilder lineage validation
  -> QuantitativeEvidenceAssessor
```

`SourceAnchor` содержит `SourceMaterialId`, версию нормализации, canonical
`StartOffset`/`EndOffset`, нормализованный текст span и SHA-256. Нормализация
использует FormKC, сворачивает whitespace, trim и lower-case. Поэтому offsets
относятся к canonical text, а не к исходному provider payload. Один и тот же
anchor разрешается ровно один раз; повтор даёт `Ambiguous`, отсутствие даёт
`Unsupported`.

## Что именно проверяется

| Поле | Условие `Verified` |
| --- | --- |
| EffectMeasure | известный alias (OR/RR/HR/MD/SMD/RD/correlation) в одном локальном предложении |
| EffectEstimate | исходное числовое значение рядом с тем же effect measure |
| ConfidenceInterval | CI/confidence interval, обе границы и effect value в одном локальном контексте |
| ConfidenceLevel | заявленный уровень рядом с CI |
| PValue | `p` + оператор + значение; при наличии effect те же measure/value |
| StandardError | `SE`/`standard error` и значение в локальном контексте |
| SampleSize | распознанная конструкция `n = N`, `N participants` и т.п.; arm/subgroup контекст отвергается |
| Outcome/Population/Comparator | exact normalized phrase match внутри anchor; это не ontology matching |

Для p-value оператор берётся из source. Если LLM прислал конфликтующий
оператор, p-value получает `Unsupported`, число удаляется из accepted
quantitative data, а ошибочный оператор не сохраняется. Нормализация оператора
сохраняет `=`, `<`, `>`, `<=`, `>=`.

## Статусы и количественная граница

`Verified` означает, что проверенный факт прошёл deterministic rules.
`Ambiguous` означает несколько подходящих локальных контекстов.
`Unsupported` означает отсутствие достаточной связи. `NotApplicable` остаётся
допустимым для поля, которое не было заявлено. Ни один LLM output не может
изменить статус на `Verified`.

`Evidence.NumericGrounding` и `PValueOperator` сохраняются в PostgreSQL JSONB
и отдельном varchar(2). Добавлена миграция
`20261005125712_AddSourceAnchoredNumericGrounding`. При чтении corpus проверяется
совпадение anchor с `EvidenceExtraction.SourceMaterialId`, bounds, длина и hash.
Количественная оценка требует `Verified` для фактически используемых measure,
estimate, CI/SE и sample size для correlation. Исторические строки без
grounding metadata не считаются автоматически verified; старые unit fixtures
без metadata оставлены для совместимости тестового конструктора, но production
persisted JSON имеет `[]` и блокируется quantitative gate.

Формулы и контракты M17-M24 не менялись. LLM не считает ни pooled effect, ни
quantitative eligibility.

## F12 и hostile cases

Validation-guided repair по-прежнему ограничен существующим числом попыток и
после repair повторно проходит тот же `EvidenceExtractionDraftValidator`.
Добавлены проверки: повторяющийся одинаковый anchor, полный OR/CI/p-value
пример, Frankenstein effect/CI/p-value из разных предложений, неправильный
effect measure, sample size из intervention arm, оператор p-value `<`, а также
конфликтующий оператор из LLM. Такой подход не утверждает causal correctness и
не решает semantic equivalence разных терминов.

## Верификация и ограничения

На локальном окружении solution build и focused Application tests проходят.
PostgreSQL round-trip тест остаётся реальным Testcontainers тестом; если Docker
Desktop недоступен, локальный запуск честно skip, а authoritative CI должен
выполнить его на fresh PostgreSQL. Live OpenAI/PubMed вызовы для F15 не нужны и
не запускаются.

Главное оставшееся ограничение: local sentence rules не являются полноценной
моделью scientific entailment. Outcome/population/comparator проверяются
консервативным exact normalized matching, отдельного timepoint поля нет, а
правильность внешнего scientific текста не подтверждается одним фактом его
наличия в SourceMaterial.
