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