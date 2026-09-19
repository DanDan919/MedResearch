# Discoveries

## 2026-08-30

- The installed SDK was .NET 10.0.302 during initial setup; by 2026-09-02 the local SDK is .NET 10.0.400.
- `dotnet new sln` created `MedResearch.slnx`, the newer solution format supported by the installed SDK.
- GitHub CLI (`gh`) is not installed in this environment.
- Docker CLI and Docker Compose are installed, but the Docker Desktop engine was not reachable during persistence setup.
- Testcontainers requires the Docker engine and should not be treated as a pure unit-test dependency.
- EF Core migrations can be created from Infrastructure with API as startup when both projects have private design-time tooling as needed.


## 2026-09-02

- Current NCBI E-utilities documentation confirms base URL `https://eutils.ncbi.nlm.nih.gov/entrez/eutils/`, PubMed database name `pubmed`, optional `api_key`, default request limits of 3 requests/second without an API key and 10 requests/second with an API key, encouraged `tool`/`email` identification, and batching/History Server guidance for larger retrieval jobs.
- Current Europe PMC Articles REST API documentation confirms production REST base `https://www.ebi.ac.uk/europepmc/webservices/rest/`, search endpoint `/search`, `query` parameter, `format=json`, result types `idlist`, `lite`, and `core`, and cursor pagination through `pageSize`, `cursorMark`, and `nextCursorMark`.
- Europe PMC `core` search results expose source/id provider identity plus publication metadata such as PMID, PMCID, DOI, title, abstract text, authors, journal fields, publication dates, and publication types. This is enough for the current abstract-level search pipeline without a per-record detail request.
- Europe PMC does not use a PubMed-style `api_key` for normal Articles REST search. MedResearch keeps Europe PMC request pacing as a conservative local configuration rather than treating it as an authenticated quota.

## 2026-09-16

- Quantitative readiness requires more than a reported number. MedResearch now treats source-reported statistics, normalized statistics, and future pooled estimates as separate concepts.
- A confidence interval is not enough to derive a standard error unless the confidence level is explicitly reported; normal CI conventions are not assumed.
- Multiple Evidence items from one Study may be useful descriptively, but they are not independent study contributions for future quantitative synthesis.

## 2026-09-16 M16

- A real live E2E check needs a separate explicit gate because it combines paid OpenAI calls, live scientific providers, and a writable PostgreSQL database. Keeping it outside `MedResearch.slnx` preserves deterministic normal tests.
- Planning needed a configurable query-count bound so live validation can cap provider fan-out without relying on the LLM to voluntarily return only two queries.

## 2026-09-17 M17

- Cochrane Handbook guidance supports generic inverse-variance synthesis using intervention effects and standard errors, and ratio measures such as OR/RR/HR should be analyzed on the log scale before back-transformation.
- The term fixed-effect/common-effect describes a model assumption for the pooled estimate; it does not prove homogeneity. Random-effects modeling and richer heterogeneity interpretation must remain explicit future work rather than implied by a pooled V1 result; M19 later addressed tau-squared estimation only as foundation data.
- M17 keeps pooled quantitative synthesis as a deterministic Application read model because persisted Evidence/SourceMaterial lineage plus algorithm version is enough for reproducibility at this stage.
## Milestone 18 Discoveries

- Cochran's Q is only meaningful for MedResearch when it is calculated on the same analysis scale and with the same weights as the fixed-effect synthesis result it describes. For current OR/RR/HR support, that means log-scale effects and inverse-variance weights.
- I-squared is best represented internally as a proportion (`0..1`) to avoid ambiguity between `0.5` and `50%`. Presentation can convert later if needed.
- `Q <= df` and `Q == 0` need explicit handling; otherwise a direct `(Q - df) / Q` implementation can produce negative I-squared or divide by zero.
- Keeping diagnostics transient alongside M17 avoids adding persistence before the product has a stable quantitative report/API representation.
## Milestone 19 Discoveries

- Cochrane Handbook guidance describes tau-squared as the between-study variance and notes REML as the current RevMan default estimator, but M19 must not imply that a random-effects pooled estimate exists.
- REML tau-squared estimation can be added as pure deterministic Application code over the same M17 contribution set; persistence is unnecessary while quantitative outputs remain transient read models.
- A boundary estimate of tau² = 0 is different from numerical non-convergence. MedResearch now represents non-estimation explicitly instead of silently returning zero.
- The `metafor` BCG example is a useful independent reference dataset: `escalc(measure="RR")` followed by `rma(yi, vi, method="REML")` reports tau² approximately 0.3132.
