# F16: структурная авторитетность научных утверждений

Дата: 2026-10-08. Это журнал реализации и проверки, а не обещание универсального entailment.

## 1. Baseline и граница аудита

Фактический starting HEAD: `d5d3c5c02bccdbcd37b6ea7f36c9be02566bfe3c`.
Ветка `main`, upstream `origin/main`, remote `https://github.com/DanDan919/MedResearch.git`.
Рабочее дерево до правок было чистым; baseline проверен status/branch/HEAD/upstream/remotes/log30/diff check. История не переписывалась. Изучены инструкции, архитектура, synthesis/provenance/quantitative contracts, stores, prompt и соответствующие UI/tests. Статистические алгоритмы не перерабатывались.

Старый `ResearchReportClaim` содержал role, direction, свободный `Text`, ordinal и отдельные ссылки Evidence. Validator проверял существование Evidence в context, часть direction/category правил и отсутствие model-supplied PMID/DOI. Положительному claim было достаточно одного положительного finding; наличие цитат не проверяло, что текст утверждает именно поддерживаемое содержание. Разделы отчёта тоже были модельным текстом.

## 2. Реальный red baseline

До изменения production-кода добавлены десять rejection tests:

| Контрпример | Старый результат |
| --- | --- |
| Направление в enum соответствует вреду, текст утверждает пользу | принят |
| Подмена outcome | принят |
| Подмена population | принят |
| Подмена intervention | принят |
| Подмена comparator | принят |
| Подмена timepoint | принят |
| OR 0.63 вместо 0.73 | принят |
| 0.73 названо p-value, 0.03 названо OR | принят |
| Positive + NoClearEffect названо consistent benefit | принят |
| InsufficientEvidence названо definitive benefit | принят |

Реальный первый запуск: **10 failed, 0 passed**, поскольку ожидаемое exception не возникло. Локальный TRX: `TestResults/F16-Red/F16-red.trx` (ignored). Не утверждается, что старый код пропускал любые возможные cross-run attacks: часть таких проверок уже существовала.

## 3. Структурная модель

`ResearchClaimSemantics`, protocol `structured-claim-v1`, хранит:

- kind: QualitativeEffect, MixedEvidence, ReportedStudyResult, QuantitativeSynthesis, InsufficientEvidence;
- outcome, population, exposure/intervention, comparator, timepoint;
- закрытый direction и EvidenceIds;
- NumericEvidenceId либо QuantitativeArtifactId;
- GroupKey, SnapshotFingerprint, statistic selector и backend numeric snapshot.

Role Finding/Conflict/Limitation/Conclusion сохранён отдельно от научного kind. Domain проверяет закрытые категории и согласованность формы/ref-kind; Application проверяет поддержку. GroundingStatus: StructuredValidated или LegacyUnverified. Nullable scope не превращается в wildcard. Методологические/каузальные claims не включены в новую мини-онтологию.

Draft содержит только выбор category/scope/reference. Schema не предоставляет Text или numeric values. Для совместимости десериализации старые draft-поля ещё существуют, но ненулевой Text/неизвестные claim fields отклоняются. Нельзя прислать estimate=0.63, pooledEstimate, PMID, DOI или StudyId как источник научной истины.

## 4. Детерминированная валидация

Scope сравнивается после whitespace normalization, case-insensitive, без синонимов и расширения population. Все пять context labels должны совпадать со всеми cited findings. Слова «healthy adults» нельзя заменить на «patients» или «all adults»; «6 weeks» нельзя заменить на «long-term». Null совпадает только с отсутствием значения. Overlong scope отклоняется, не обрезается.

Uniform QualitativeEffect требует одного согласованного direction у всех cited Evidence. Positive + Negative/NoClearEffect/NotReported не становится uniform Positive. MixedEvidence требует Mixed direction и реально различающихся directions либо reported Mixed. Допустима честная compatible subset; renderer явно говорит **within the cited Evidence**, не **all studies**. Direction Positive/Negative отражает сохранённую категорию, не независимо доказанную клиническую пользу/вред.

InsufficientEvidence report допускает только отсутствие claims либо run-level InsufficientEvidence metadata claim с NotApplicable, без scope/чисел/IDs. NoValidatedEvidence требует пустого validated context. Нулевой corpus завершает synthesis без обращения к LLM. Отсутствие Evidence не превращается в NoClearEffect или proof of no effect.

Evidence IDs должны принадлежать текущему bounded trusted context. Cross-run context и corrupt/foreign artifact fingerprint являются non-repairable invariant failures; unknown proposal reference repairable, но не расширяет поддержку.

## 5. Числа, CI/PI, Wald/HKSJ

ReportedStudyResult ссылается ровно на один NumericEvidenceId, совпадающий с единственной citation. `SynthesisEvidenceProjection` повторно проверяет source-grounded tuple predicates. Выбрать можно только действительно доступные StudyEffect, StudyConfidenceInterval, StudyStandardError, StudyPValue, StudySampleSize. Для CI нужны обе границы и confidence level. Missing/rejected statistic не подставляется из prose или prior knowledge.

QuantitativeSynthesis ссылается на **реально persisted** current-run ArtifactId и all-and-only contribution Evidence set. Store persist port возвращает read models с фактическими IDs/fingerprints; модель не выдумывает artifact ID. Selectors: FixedEffectWald, RandomEffectsWald, RandomEffectsHksj, RandomEffectsPredictionInterval, CochransQ, ISquared, TauSquared. Отсутствующий/неоценённый/non-finite selected result отклоняется. Источник pooled estimate не смешивается с отдельным study.

Backend копирует значения из immutable artifact, не перепечатывает модельные числа и не пересчитывает формулы. Study values сохраняют decimal, artifact values double. Отображение invariant: decimal G29, double R; display CI level умножается на 100 backend formatter, научные stored values не округляются моделью. I² обозначается как proportion; tau² как analysis-scale variance, не процент/quality score. Wald и HKSJ используют сохранённые method-specific CI при общей RE point estimate. PI назван prediction interval, не confidence interval.

## 6. Renderer и authority

`StructuredResearchClaimRenderer` строит bounded core sentence с exact scope. Качественная фраза не утверждает causality/clinical significance. Mixed rendering исключает uniform effect; NoClearEffect исключает proof of no effect; insufficiency исключает effect conclusion.

ExecutiveSummary/EvidenceSummary/ConflictSummary/LimitationsSummary/Conclusion формируются backend по проверенному context/claims. Старые model prose sections отбрасываются; никаких authoritative optional explanatory paragraphs не сохраняется. SynthesisConfidence остаётся внутренней модельной категорией существующего контракта, не formal GRADE и не новая оценка клинической уверенности.

Semantic key: SHA-256 canonical structured semantics со sorted Evidence IDs и case-normalized scope. Duplicate scientific semantics, даже под разными report roles, отклоняются. Это дедупликация структуры, не дедупликация синонимов.

## 7. F12 repair

Используются существующие ValidationIssue/ValidationGuidedLlmRepairService. Новые реально применённые codes: ClaimOutcomeMismatch, ClaimPopulationMismatch, ClaimInterventionMismatch, ClaimComparatorMismatch, ClaimTimepointMismatch, MixedEvidenceOverstated, UnsupportedNumericAssertion, QuantitativeArtifactMismatch, InsufficientEvidenceOverclaim. UnknownEvidenceReference/InvalidDirection/SynthesisContractViolation и другие существующие codes сохранены.

Один initial candidate + максимум один replacement. Тот же trusted prompt/context, Evidence/artifacts и schema; в repair добавляются typed instructions, не внешняя поддержка. Replacement валидируется с нуля. Невалидный второй candidate fails closed; третьего запроса нет. Provider failures/cross-run corpus не исправляются моделью.

## 8. Persistence, legacy и concurrency

Forward migration: `20261008072325_AddStructuredReportClaims`. Предыдущие migrations не редактировались.

Новые claim columns: grounding_status, semantics JSONB, semantic_key, numeric_evidence_id, quantitative_artifact_id. Text max увеличен 800 -> 4000 для explicit scope. FK на numeric Evidence/artifact Restrict; unique filtered `(research_report_id, semantic_key)`; existing ordinal uniqueness сохранена. CHECK различает legacy null-shape и structured protocol/key/ref-column coherence. Down сжимает text обратно и может быть неприменим к длинным новым sentences; штатный deployment forward-only, не обещание lossless rollback.

`EfResearchSynthesisStore` в короткой transaction проверяет lease fence, existing report idempotency, точную same-run Evidence -> extraction -> source lineage, rebuilds trusted corpus и reads immutable artifacts. Accepted result повторно превращается в reference-only draft, валидируется и сравнивается с expected numeric semantics/text/sections. Подмена уже AcceptedResearchReportClaim не считается доверенной. В transaction нет внешних LLM/HTTP calls.

Same-run связи защищаются application/store/read guards, не универсальным PostgreSQL cross-table constraint. Structured read требует совпадения filtered current-run citations с semantics, key/text/direction/ref columns и current-run artifact GroupKey/fingerprint. Corrupt structured row fails closed, не downgraded в legacy. Direct privileged SQL не является поддерживаемым scientific write protocol.

Existing run/prompt idempotency сохранена: повтор возвращает existing report, не перезаписывает accepted scientific snapshot. Lease owner/version fencing защищает и structured report; stale owner не пишет новый report после takeover. Новая схема не меняет lease алгоритм.

Исторические rows получают LegacyUnverified и null semantics/ref columns. Сфера/числа/entailment задним числом не фабрикуются. Старые handwritten report-store fixtures явно используют v2, а новый structured path имеет отдельные реальные PostgreSQL cases.

## 9. API, provenance и frontend

GET report и GET provenance возвращают GroundingStatus/Semantics с named categories, context, numeric/artifact references. Report дополнительно сообщает NarrativeAuthority: StructuredClaims для нового protocol/prompt или LegacyUnverified. Claim -> Evidence -> extraction -> exact SourceMaterial -> authoritative Study metadata остаётся traceable. SourceMaterial.Content не добавлен в read models.

Frontend показывает backend sentence как primary assertion, status badges, expandable Claim support с пятью context fields, Evidence IDs/fingerprint и ссылками на existing Evidence/Quantitative workspace. Не считает CI/PI/weights/claim meaning. Legacy narrative/claims явно unverified. Raw ResultSummary в supporting disclosure обозначен **Unverified extracted summary**, не источник authoritative claim.

Zod закрывает новые kind/direction/role/status/statistic values, UUID, bounded scope, finite numeric snapshot и coherence ref-kind/status. Unknown values/missing authority fail safely. Это transport validation, не второй scientific calculator.

Реальный API временно запущен на loopback 5098 с disabled ResearchProcessing; получен `/openapi/v1.json`. Только changed report/provenance schemas механически импортированы в curated snapshot через structured JSON/TypeScript AST; несвязанный контракт не переформатирован. `pnpm api:generate` сгенерировал TS штатно; generated TS вручную не редактирован. Полный backend-origin drift gate пока отсутствует, это оставшийся риск.

## 10. Тесты и локальная верификация

37 новых Application cases: исходные 10 prose attacks, valid constrained qualitative, exact field mismatches, missing scope, differing direction/mixed subset controls, insufficiency/absence, grounded tuple selection, model numeric fields, wrong statistic/reference, exact pooled selectors, Wald/HKSJ/PI shared-point/different-interval controls, unknown/cross-run refs, duplicate semantics, bounded repair success/failure без third attempt.

5 новых Domain cases: explicit historical authority и incoherent kind/ref/direction/numeric shape. 9 новых PostgreSQL cases: structured fresh-context/provenance roundtrip+idempotency, четыре bypass attacks (Text/scope/sections/unstructured), corrupt stored text fails read, legacy preservation, empty corpus truthful completion, stale report writer after takeover.

Существующий fake-provider full vertical E2E расширен: Question -> planning -> search -> source acquisition -> extraction -> evaluation -> synthesis -> report -> GET report/provenance. Он включает qualitative и RE Wald/HKSJ/PI claims, all contribution citations, actual ArtifactId/fingerprint/values. Fakes: structured LLM, scientific literature source и source-acquisition fixture; никаких live scientific/paid calls.

| Проверка | Локальный результат |
| --- | --- |
| restore/build | passed, 0 warnings/errors |
| Domain | 45 passed |
| Application | 289 passed |
| Infrastructure | 110 passed |
| Integration | 26 passed, 102 Docker skips |
| Все failures | 0 |
| Отдельный F15.1 trust-boundary filter | 61 passed |
| EF pending-model | no pending changes |
| Compose config --quiet | passed |
| docker info | failed: Linux engine pipe unavailable |
| frozen pnpm install / api:generate / lint / typecheck | passed |
| Frontend API | 31 passed |
| Frontend web | 33 passed |
| Chromium | 14 passed, включая hydration regression |
| Production Next build | passed |
| Desktop React/Vite build | passed; native Tauri НЕ проверена |
| Live projects build/gates | 0 passed, 0 failed, 6 deliberate opt-in skips |

Debug/Release TRX сохранены локально под ignored TestResults. После последних Domain-only shape controls полный regression повторяется перед commit. Local skips не являются PostgreSQL verification. CI требуется отдельно.

## 11. Live claim audit

**NOT RUN**. Локальный Docker/изолированный PostgreSQL stack недоступен. Не запускался реальный Codex/OpenAI/PubMed/Europe PMC workflow. ResearchRunId, report status, claim count и manually audited live claims: N/A. Live false-positive/false-negative rate не измерен, а не «равен нулю». Детерминированные positive/negative controls не называются live scientific validation.

## 12. CI и Git (заполняется после проверки)

Production implementation ещё не объявляется COMPLETE до реального green CI. Workflow `.github/workflows/ci.yml`, ubuntu-latest, .NET 10.0.x, Node 24.x, pnpm 11.19, PostgreSQL/Testcontainers с `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`. Стандартный workflow не содержит live providers/keys и запрещает required backend skips.

Ожидаемые counts не считаются результатом: окончательные counters/CI ID/commit будут прочитаны из GitHub после push. Final classification и exact Git state будут записаны здесь отдельным verification update.

## 13. Самокритика и remaining risks

Citation по-прежнему не является entailment. Устранён воспроизведённый обход свободным primary claim prose; не доказаны произвольная semantic equivalence, сложное population inclusion, causal wording, clinical magnitude, observational-study interpretation и factual correctness upstream Evidence/provider. Conservative exact-label comparison намеренно может отвергать допустимые перефразы. Нарратив становится уже и технически подробнее; это сознательный precision-first tradeoff.

Поле qualitative direction не превращено в benefit/harm calculator. SynthesisConfidence не стало формальным certainty. SemanticKey не NLP similarity detector. Legacy text остаётся исторически unverified, даже если citations настоящие. Administrator SQL может испортить scientific state; поддерживаемые writes выполняются через validators/fence. Набор тестов конечен и не означает «галлюцинации решены».

Формулы M17-M24, grouping compatibility, tuple/source-proof и provider runtime boundaries не изменены. F15.1 61 regressions повторно green; F15.2 suite входит в full regression. Статистические selector tests используют явно synthetic fixture values только для проверки копирования/labels, а не в качестве нового independent mathematical oracle. Существующий real-PostgreSQL fake E2E проверяет связи с actual deterministic calculator artifacts.

Remaining audit P2/P3: production browser token flow не подключён в `createApiClient`, native Tauri build, mobile shell overflow, dependency advisories, whole-backend OpenAPI drift gate и acquisition failure provenance не исправляются внутри F16. Credentials/auth files не читались для live calls и не добавлялись в diff; staging review обязателен.

## 14. Ответы на ключевые вопросы и следующий шаг

До F16: valid citation мог сопровождать семантически неверный free-text claim. После F16 authoritative core выражается только реализованными structured rules: нельзя подменить direction/outcome/population/intervention/comparator/timepoint относительно cited Evidence, добавить model pooled value, назвать differing cited Evidence uniform или insufficiency no effect. Числа происходят из проверенной persisted Evidence/artifact state; свободный prose не авторитетен. Более широкая семантика реальных исследований остаётся отдельным пределом, а не скрытым доказанным свойством.

Рекомендован ровно один следующий milestone: **F17 Production Web Authentication Flow**. Актуальный `frontend/apps/web/components/api-client-provider.ts` создаёт SDK без getAccessToken; production API имеет JWT boundary, но browser login/token acquisition workflow пока отсутствует. Это конкретный release blocker, не новая scientific capability. Выбор identity provider, безопасный session/token lifecycle и deterministic owner/auth browser tests должны составлять один отдельный coherent milestone. Он здесь НЕ начат.
