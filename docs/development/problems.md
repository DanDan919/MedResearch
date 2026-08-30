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
Problem: The local repository initially could not be pushed to the configured GitHub remote.
Observed behavior: Early `git ls-remote --heads origin` and `git push -u origin main` attempts returned `Repository not found` for `https://github.com/DanDan919/MedResearch.git`. A later attempt failed with a network connection error to `github.com:443`.
Root cause: The repository or credentials were not ready at first, and the local environment later had a transient network failure.
Decision / fix: Kept the configured remote URL because it matched the requested owner/repository path. After explicit approval to export repository contents to the exact remote, `git push -u origin main` succeeded.
Verification: `origin/main` existed at `bf85aa5e30ac81d20b83ea071a010f9cf412c915`, and local `main` tracked `origin/main`.
Remaining concerns: None for the configured remote at the time of that verification.
