# Milestone 14 — Evidence Corpus & Source Traceability

## 1. С чего начали

Фактическая исходная точка: ветка main, HEAD 2496029 (feat: add Europe PMC multi-source retrieval). Рабочее дерево уже содержало незакоммиченный слой SourceMaterial и Europe PMC structured full text от предыдущего этапа. Эти изменения были сохранены и продолжены.

## 2. Общая архитектура

ResearchQuestion -> ResearchRun -> Planning -> Searching -> Study -> SourceMaterial -> EvidenceExtraction -> Evidence -> EvidenceEvaluation -> EvidenceCorpus -> Synthesis -> ResearchReport.

Domain остаётся независимым от EF/HTTP. Application задаёт порты и trust boundaries, Infrastructure владеет PostgreSQL и внешними API, Api остаётся composition root и HTTP-слоем.

## 3. Study

Study описывает глобальную identity публикации. Для deterministic matching используются нормализованные PMID, PMCID и DOI. Title, авторы и год не используются для fuzzy merge.

## 4. SourceMaterial

SourceMaterial описывает точную representation, которой располагал MedResearch: abstract или bounded structured full text. В нём сохраняются provider, retrieval method, provider source id, hash, version, access status, sections и truncation.

## 5. Evidence

Evidence является finding конкретного ResearchRun. Completed EvidenceExtraction хранит SourceMaterialId, поэтому finding можно связать с exact source snapshot, а не только с текущим Study.Abstract.

## 6. EvidenceCorpus

EvidenceCorpusBuilder — application-level read model и проверка перед LLM synthesis. Он проверяет run scope, source lineage, grounding flag, уникальность Study и связи Evaluation с Evidence.

## 7. Provenance

ResearchReportClaim -> Evidence -> EvidenceExtraction -> SourceMaterial -> Study -> PMID / PMCID / DOI.

Идентификаторы citation берутся из persistence, а не из ответа модели.

## 8. Несколько источников

Один query исполняется отдельно через PubMed и Europe PMC. Это две LiteratureSearch записи и отдельные ResearchStudyDiscovery paths. При совпадении stable identity сохраняется один canonical Study, а downstream corpus содержит один Study snapshot.

## 9. Abstract vs Full Text

Если доступен current usable non-truncated StructuredFullText, он выбран первым. Иначе используется current Abstract. Если source material нет, extraction получает NoExtractableText, LLM не вызывается, а run не объявляется научно проваленным.

## 10. Версии SourceMaterial

Content нормализуется только по line endings и outer whitespace, затем хешируется SHA-256 по UTF-8. Новое содержимое создаёт новую version; старая запись не переписывается и остаётся доступной для старого Evidence.

## 11. Изоляция ResearchRun

Study и SourceMaterial могут быть shared. ResearchPlan, LiteratureSearch, ResearchStudyDiscovery, EvidenceExtraction, Evidence, EvidenceEvaluation, ResearchReport и claims scoped к run. EvidenceCorpus запрещает rows другого run.

## 12. Синтез

В LLM поступает bounded deterministic context, подготовленный через validated corpus. Текущий режим — NarrativeEvidenceSynthesis; это не statistical meta-analysis.

## 13. Почему нельзя просто усреднить EffectValue

OR 1.4, Cohen d 0.3, mean difference 4.2 и correlation 0.25 имеют разные шкалы и статистический смысл. Даже одинаковый measure требует информации о variance, sample size и модели. Простое Average(EffectValue) создало бы математически недостоверный результат.

## 14. Reliability

Background worker использует lease owner, expiry, heartbeat и fencing version. После передачи lease старый worker не должен записать новое состояние. Source acquisition выполняется внутри обычного stage execution и не обходит эту защиту.

## 15. Tests

Application tests проверяют cross-run rejection, cross-study SourceMaterial rejection, deterministic outcome grouping and conflict preservation. PostgreSQL tests проверяют persistence, source versioning и полный reload graph; Docker-backed тесты запускаются в CI.

## 16. Ограничения

Нет PDF/HTML scraping, publisher crawling, paywall bypass, embeddings/RAG или statistical meta-analysis. Полнотекстовый слой ограничен Europe PMC official structured XML и bounded input. Live external smoke tests остаются opt-in.

## 17. Следующий этап

Добавить отдельную persisted acquisition-attempt/diagnostic модель только если эксплуатация покажет, что логов недостаточно; затем отдельно спроектировать quantitative eligibility и meta-analysis boundary, не смешивая её с narrative synthesis.
