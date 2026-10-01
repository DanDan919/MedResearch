# F8: полный adversarial-аудит MedResearch

Дата аудита: 2026-10-01
Репозиторий: `E:\MedResearch`
Ветка: `main`
Базовый HEAD: `44c30446f0aa358bddeb72926037857b5d4d187a`
Предыдущий подтверждённый F7 CI: [36680132292](https://github.com/DanDan919/MedResearch/actions/runs/36680132292)

## 1. Цель и метод

F8 был проведён как adversarial-проверка существующей реализации, а не как проектирование нового продукта. Код проверялся как источник истины; README, `ARCHITECTURE.md`, ADR-001--ADR-022, development-документы, EF-модель, API, frontend и тесты использовались как вспомогательные свидетельства.

Для каждого важного свойства применялась классификация:

- **VERIFIED**: подтверждено кодом и релевантным тестом;
- **PARTIAL**: гарантия существует только на application/store boundary либо имеет известный обход;
- **TEST_ONLY**: есть тест, но нет более сильного runtime enforcement;
- **DOC_ONLY**: описано, но не подтверждено реализацией;
- **STALE**: прежнее описание не соответствует коду;
- **UNKNOWN**: в репозитории нет достаточного доказательства;
- **CONTRADICTED**: код нарушает заявленное свойство;
- **HALLUCINATED**: заявленное свойство не найдено ни в коде, ни в тестах.

## 2. Краткий результат

Архитектура в целом согласована: Domain не знает EF/HTTP, Application использует provider-neutral контракты, Infrastructure владеет PostgreSQL и внешними адаптерами, API остаётся composition root. Study глобален, а Evidence/Extraction/Evaluation/Report run-scoped. PubMed и Europe PMC сохраняют отдельные LiteratureSearch и ResearchStudyDiscovery, а downstream work deduplicates Study.

До F8 были найдены четыре существенных write-path разрыва:

1. lease owner/version защищали переход `ResearchRun`, но не сохранение stage output;
2. quantitative contribution не требовала exact `EvidenceExtraction.SourceMaterialId`;
3. свободные compatibility keys могли столкнуться из-за разделителя `|`;
4. прямой `IResearchReportStore` мог принять cross-run или неподтверждённую Evidence-ссылку.

Они исправлены узко, без миграции и без изменения научной функциональности. Добавлены PostgreSQL-регрессии для stale artifact writer, exact source lineage, drift relational snapshot и cross-run report citation. До запуска Docker-backed CI эти исправления имеют статус **PARTIAL/TEST_ONLY**, а не VERIFIED.

## 3. Фактическая карта системы

```text
POST /api/research
  -> CreateResearchUseCase
  -> EfResearchStore
  -> ResearchRun(Queued)
  -> BackgroundResearchWorker
  -> PostgreSqlResearchRunQueue
  -> ResearchRunProcessor
  -> ScientificResearchStageExecutor
     -> ResearchPlanner -> IResearchPlanStore -> ResearchPlan
     -> ScientificLiteratureSearchCoordinator
        -> PubMed / Europe PMC adapters
        -> IScientificSearchResultStore
        -> Study + LiteratureSearch + ResearchStudyDiscovery
     -> SourceMaterialAcquirer -> ISourceMaterialStore
     -> EvidenceExtractor -> IEvidenceExtractionStore
     -> EvidenceEvaluator -> IEvidenceEvaluationStore
     -> SynthesisContextBuilder
        -> EvidenceCorpusBuilder
        -> deterministic quantitative assess/synthesis
        -> IQuantitativeSynthesisArtifactStore
        -> ResearchSynthesizer
     -> IResearchReportStore
GET /api/research/{id}/report
GET /api/research/{id}/quantitative
```

`ResearchRunProcessor` больше не передаёт только логический `ResearchRunId`: scoped `IResearchRunWriteFence` прикрепляет конкретного owner/version claim. Все production EF stage stores проверяют claim внутри собственной короткой PostgreSQL-транзакции до записи. Внешние HTTP/LLM вызовы не выполняются под открытой DB-транзакцией.

## 4. Зависимости и границы

| Свойство | Результат | Свидетельство |
|---|---|---|
| Domain не зависит от Application/Infrastructure/API | VERIFIED | project references, namespaces |
| Application не зависит от DbContext/DbSet/NCBI/OpenAI DTO | VERIFIED | project references и контракты |
| Infrastructure владеет EF, PostgreSQL и HTTP adapters | VERIFIED | Infrastructure registrations и stores |
| API не содержит pipeline/scientific persistence rules | VERIFIED | `Program.cs` только endpoint mapping/DI |
| LLM output считается untrusted | VERIFIED | schema + planner/extraction/evaluation/synthesis validators |
| frontend не считает статистику | VERIFIED | F7 frontend tests/source scan; values приходят как persisted artifacts |

## 5. Lifecycle и worker recovery

Легальные transitions: `Queued -> Planning -> Searching -> Extracting -> Evaluating -> Synthesizing -> Completed`; ошибки идут в `Failed`, host cancellation не маскируется под scientific failure, `Cancelled` и terminal states не reclaimable. Domain methods блокируют произвольные переходы. Queue использует PostgreSQL `FOR UPDATE SKIP LOCKED`, expiry, heartbeat и monotonic `ProcessingLeaseVersion`.

| Атака | Результат |
|---|---|
| Completed/Failed/Cancelled reclaim | VERIFIED: queue tests и status policy |
| два worker claim одного run | VERIFIED: existing PostgreSQL concurrency tests |
| stale queue progress/failure/release | VERIFIED: owner + lease version predicates |
| stale worker после передачи lease пишет artifact/report | PARTIAL -> исправлено scoped write fence, новые real-PostgreSQL tests ожидают CI |
| lease transfer во время stage transaction | VERIFIED по конструкции fence: row lock и owner/version проверяются в той же транзакции |
| heartbeat остаётся вне stage transaction | VERIFIED: queue использует отдельный context; pipeline не держит lock через network I/O |

Remaining operational boundary: direct SQL, обходящий application stores, не обязан соблюдать write fence. Это сознательно не решалось триггерами или отдельной очередью.

## 6. Provenance и cross-run isolation

`Study` дедуплицируется по нормализованным PMID/PMCID/DOI; title/author/year matching отсутствует. Hard conflict нескольких стабильных identity graphs не merge-ится: существующие Studies сохраняются, discovery пропускается с bounded log. Один Study может иметь несколько source/search discovery paths. Evidence, Extraction, Evaluation, Report и quantitative artifacts проверяют run scope.

Проверенные атаки: два источника для одного PMID, два query для одного Study, no-ID records, same PMID/conflicting DOI, same Study в двух runs, cross-run Evidence в corpus/report. Результат: **VERIFIED** для существующих search/corpus tests; direct report store до F8 был **CONTRADICTED**, теперь отклоняет cross-run/ungrounded citation до `SaveChanges`.

Известное ограничение: same-run citation invariant всё ещё application/transaction enforcement, а не composite PostgreSQL FK, потому что join table не хранит redundant `ResearchRunId`.

## 7. LLM trust boundary

Planner получает только текущий question и обязан вернуть bounded ResearchPlan. Extraction получает выбранный SourceMaterial snapshot; supporting text обязан быть substring source. Evaluation разделяет `Unknown`, `InsufficientSource`, `NotApplicable`; synthesis принимает только validated current-run corpus. Model-supplied PMID/DOI/StudyId запрещены.

Это не является доказательством научной семантики. Numeric grounding сейчас доказывает наличие numeric token в source text, но не причинно-статистическую связь токена с полем. Это **PARTIAL**, оставлено как честно описанный риск. Также нет автоматического доказательства, что LLM классификация design/limitations методологически верна.

## 8. Quantitative artifact integrity

Числа вычисляются deterministic Application code; LLM не считает HKSJ, Wald, Q, tau² или prediction interval. F7 workspace только отображает persisted artifact.

F8 hardening:

- contribution обязан ссылаться на тот же `SourceMaterialId`, который записан в EvidenceExtraction;
- extraction должен быть `Completed` и `GroundingValidated`, Evidence также должен быть grounded;
- `GroupKey` теперь length-prefixed, поэтому `a|b + c` не сталкивается с `a + b|c`;
- read path сравнивает JSON snapshot с relational header, fingerprint, method rows, ordinals, IDs и всеми числовыми полями;
- ordinal назначается после canonical sort, а не до него;
- artifact JSON должен содержать запрошенный run id и GroupKey.

Immutability/idempotency по `(ResearchRunId, GroupKey, SnapshotFingerprint)` и exact lineage имеют **VERIFIED**-семантику на store boundary; real PostgreSQL tamper/retry regression ожидает CI.

Отдельный риск: ResearchReport пока не хранит artifact id/fingerprint. Связь artifact и narrative report логически выполняется в одном synthesis call, но не является persisted foreign-key lineage. Статус **PARTIAL**.

## 9. API, frontend и security

Health endpoints разделяют liveness/readiness; readiness проверяет PostgreSQL и не вызывает OpenAI/PubMed. API возвращает authoritative Study metadata из persistence. Quantitative endpoint scoped по run id. Frontend использует query keys с `researchRunId`, не хранит secrets и не выполняет scientific formulas.

Authentication/authorization не реализованы в текущем приложении. Для локального development API это не скрыто; production exposure требует deployment boundary/API gateway. Статус **UNKNOWN/PARTIAL**, не объявляется security-complete.

Normal CI не требует `OPENAI_API_KEY`, NCBI credentials или live internet. В репозитории не найдено реальных credentials; `.env` не коммитится. В audit scope не выполнялась попытка credential exfiltration из внешнего процесса.

## 10. Исправления и тесты F8

Production changes:

1. `IResearchRunWriteFence` + PostgreSQL row-lock owner/version check;
2. fence подключён к plan/search/source/extraction/evaluation/artifact/report stores и processor lifecycle;
3. exact SourceMaterial/extraction lineage;
4. report persistence validation и stricter read projection;
5. relational-vs-JSON artifact consistency validation;
6. collision-safe quantitative GroupKey.

Новые тесты:

- `Assess_DoesNotMergeCompatibilityKeysThatOnlyCollideOnTheLegacyDelimiter`;
- `RejectsContributionWhoseSourceMaterialDiffersFromExtractionSnapshot`;
- `RejectsRelationalContributionDriftWhenReadingArtifact`;
- `StaleWorkerCannotPersistArtifactAfterLeaseOwnershipChanges`;
- `PersistReportAsync_RejectsCrossRunEvidenceCitationBeforeWritingReport`.

Unit test новая группа: **10 passed**. Local Docker-backed tests: **5 quantitative и 10 report tests skipped**, потому что Docker Desktop engine недоступен. CI должен выполнить их с `MEDRESEARCH_REQUIRE_DOCKER_TESTS=true`; до этого нельзя заявлять окончательную runtime confidence.

Полный локальный .NET запуск после финальной сборки: Domain **26 passed / 0 failed / 0 skipped**, Application **163 / 0 / 0**, Infrastructure **65 / 0 / 0**, Integration **17 / 0 / 75**; всего **271 passed, 0 failed, 75 skipped**. `dotnet ef migrations has-pending-model-changes` прошёл, pending model changes нет. `docker compose config` прошёл. `docker info` подтвердил наличие CLI, но завершился ошибкой подключения к `desktop-linux` daemon, поэтому PostgreSQL/Testcontainers skips ожидаемы.

Frontend повторно проверен независимо от F8 production diff: API tests **11 passed**, web tests **22 passed**, Playwright **7 passed**, lint/typecheck/production build прошли. Во время параллельного запуска lint встретил отсутствующий Playwright `test-results` каталог; отдельный повтор после e2e прошёл.

## 11. Документированные remaining risks

- semantic numeric grounding и научная корректность LLM classification не доказываются substring validator;
- no stable identifier записи намеренно не merge-ятся;
- provider metadata conflict сохраняется консервативно, без provider ranking;
- SourceMaterial current uniqueness опирается на advisory-lock writer protocol, а не на универсальный partial unique constraint для direct SQL;
- report/artifact fingerprint linkage не хранится как отдельная связь;
- same-run citation constraint не является чистой PostgreSQL constraint;
- authentication/authorization отсутствуют;
- live provider availability не входит в normal CI;
- quantitative scope ограничен текущими M15--M24 deterministic models.

Ни один из этих пунктов не является основанием для добавления нового провайдера, RAG, embeddings или распределённой инфраструктуры в F8.

## 12. Итоговый статус F8

**PARTIAL до зелёного CI:** production fixes и adversarial tests внесены, local build/unit tests проходят, но Docker-backed PostgreSQL tests в этой среде недоступны. После CI нужно подтвердить zero failures и zero required skips, затем обновить этот отчёт фактическими run id и counts. Если CI красный, milestone не считается завершённым.

Следующий разумный milestone после закрытия F8: отдельное threat-model/API authorization hardening, а не новый scientific provider.
