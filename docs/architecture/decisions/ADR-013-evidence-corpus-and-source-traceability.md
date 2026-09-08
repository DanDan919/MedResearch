# ADR-013: EvidenceCorpus and Source Traceability

- Status: Accepted
- Date: 2026-09-08

## Context

A ResearchRun may discover one Study through multiple searches and providers. Source acquisition may also produce multiple provider representations and historical content versions. Evidence is valid only when it remains tied to the exact source representation used by extraction. The synthesis LLM must receive a bounded, run-scoped view rather than unrestricted EF entities.

## Decision

MedResearch keeps Study as global publication identity and introduces SourceMaterial as an immutable content snapshot. Abstract metadata and Europe PMC structured full text are separate snapshots with provider and retrieval provenance. Changed content creates a new version; the old snapshot remains available to historical EvidenceExtraction rows.

EvidenceCorpusBuilder is an explicit Application read-model boundary. It loads the persisted corpus through ISynthesisCorpusStore, validates ResearchRun scope and the Evidence -> EvidenceExtraction -> SourceMaterial -> Study lineage, deduplicates Studies, groups outcomes only by conservative normalized text, and computes descriptive source-coverage/conflict metrics. It does not call an LLM or perform statistical synthesis.

The existing bounded SynthesisContextBuilder consumes the validated corpus and applies synthesis limits. Claim citation identity is reconstructed from persisted Evidence and Study rows. No model-supplied PMID, PMCID, DOI, StudyId, SourceMaterialId, or EvidenceId is authoritative.

## Consequences

- Multiple discovery paths do not double-count a Study in extraction or synthesis.
- Historical scientific provenance remains reproducible after a provider changes content.
- Missing full text is a coverage limitation: abstract fallback or explicit no-source skip is used; it is not automatically a run failure.
- Structured full text does not imply higher methodological quality.
- The corpus is not persisted as a separate table. Reproducibility currently relies on immutable SourceMaterial, run-scoped Evidence, extraction references, and deterministic builder rules.
- PostgreSQL integration tests are required for graph reload, foreign keys, and concurrent persistence behavior. Normal CI remains deterministic and does not call live external providers.
