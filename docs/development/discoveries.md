# Discoveries

## 2026-08-30

- The installed SDK is .NET 10.0.302.
- `dotnet new sln` created `MedResearch.slnx`, the newer solution format supported by the installed SDK.
- GitHub CLI (`gh`) is not installed in this environment.
- Docker CLI and Docker Compose are installed, but the Docker Desktop engine was not reachable during persistence setup.
- Testcontainers requires the Docker engine and should not be treated as a pure unit-test dependency.
- EF Core migrations can be created from Infrastructure with API as startup when both projects have private design-time tooling as needed.
