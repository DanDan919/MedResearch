# Problems

## 2026-08-30

Date: 2026-08-30
Area: API template dependency
Problem: The default ASP.NET Core Web API template restored an OpenAPI package with a high-severity advisory.
Observed behavior: `dotnet new webapi` restored `Microsoft.OpenApi` transitively through template OpenAPI support and emitted advisory `NU1903`.
Root cause: The stock template included OpenAPI support that is not needed for the initial repository skeleton.
Decision / fix: Removed template OpenAPI registration and the OpenAPI package reference. Kept a minimal `/health` endpoint.
Verification: `dotnet restore`, `dotnet build --no-restore`, and `dotnet test --no-build` completed successfully.
Remaining concerns: Reintroduce OpenAPI later only with a non-vulnerable package set and a concrete API documentation need.

