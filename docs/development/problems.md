# Problems

## 2026-08-30

Date: 2026-08-30
Area: API template dependency
Problem: The default ASP.NET Core Web API template restored an OpenAPI package with a high-severity advisory.
Observed behavior: `dotnet new webapi` restored `Microsoft.OpenApi` transitively through template OpenAPI support and emitted advisory `NU1903`.
Root cause: The stock template included OpenAPI support that is not needed for the initial repository skeleton.
Decision / fix: Removed template OpenAPI registration and the OpenAPI package reference. Kept a minimal `/health` endpoint, later replaced by standard health checks.
Verification: `dotnet restore`, `dotnet build --no-restore`, and `dotnet test --no-build` completed successfully.
Remaining concerns: Reintroduce OpenAPI later only with a non-vulnerable package set and a concrete API documentation need.

Date: 2026-08-30
Area: EF Core tooling
Problem: Creating migrations with `MedResearch.Api` as the startup project failed because the startup project did not reference `Microsoft.EntityFrameworkCore.Design`.
Observed behavior: `dotnet ef migrations add InitialCreate` built successfully, then reported that the startup project must reference `Microsoft.EntityFrameworkCore.Design`.
Root cause: EF tooling loads design-time services from the startup project as well as the target project.
Decision / fix: Added `Microsoft.EntityFrameworkCore.Design` as a private design-time package reference to `MedResearch.Api` while keeping runtime persistence code in Infrastructure.
Verification: `dotnet ef migrations add InitialCreate` completed successfully afterward.
Remaining concerns: Keep design-time dependencies private and avoid moving persistence implementation into API.

Date: 2026-08-30
Area: Docker-backed integration tests
Problem: Docker CLI and Docker Desktop are installed, but the Docker engine is not reachable in this environment.
Observed behavior: `docker info` failed with a missing `dockerDesktopLinuxEngine` named pipe. `docker desktop start` hung without making the engine available. Testcontainers could not connect to `npipe://./pipe/docker_engine`. `docker compose up -d --build` failed because `//./pipe/dockerDesktopLinuxEngine` was missing.
Root cause: Docker Desktop daemon is stopped or misconfigured outside the repository.
Decision / fix: Kept PostgreSQL integration tests on Testcontainers and added `Xunit.SkippableFact` so they are explicitly skipped when Docker is unavailable. Did not switch to EF Core InMemory.
Verification: `dotnet test MedResearch.slnx --no-build` passes with PostgreSQL integration tests reported as skipped when Docker is unavailable.
Remaining concerns: Run the full integration suite, `docker compose up -d --build`, and `/health` verification again when Docker Desktop is running.

Date: 2026-08-30
Area: GitHub remote/push
Problem: The local repository cannot be pushed to the configured GitHub remote.
Observed behavior: `git ls-remote --heads origin` and `git push -u origin main` both returned `Repository not found` for `https://github.com/DanDan919/MedResearch.git`.
Root cause: The GitHub repository either does not exist or is not accessible with credentials available to this environment. GitHub CLI (`gh`) is not installed, so repository creation and authentication inspection through `gh` are unavailable.
Decision / fix: Kept the configured remote URL because it matches the requested owner/repository path. Did not create or overwrite any remote repository.
Verification: Local branch `main` remains clean with commits ready to push once the repository exists and credentials have access.
Remaining concerns: Create or grant access to `DanDan919/MedResearch`, then run `git push -u origin main`.


