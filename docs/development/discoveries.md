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
