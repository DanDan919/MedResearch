# Web Release Candidate Operations

This guide describes the deterministic web RC, not a verified deployment to a
real external IdP/environment. Native Tauri and live scientific providers are
not covered. The existing `docker-compose.yml` is development-only: it exposes
development PostgreSQL/API ports and uses DevelopmentLocal authentication.

## Trust Topology

Browser -> HTTPS reverse proxy -> private Next.js server/BFF -> private ASP.NET
JWT API -> private PostgreSQL. OIDC issuer/JWKS must be trusted HTTPS. Only the
HTTPS web origin is public. Never publish PostgreSQL or the unencrypted internal
API/Next ports to an untrusted network; use TLS on internal hops if that network
is not trusted. The proxy must preserve the exact configured Host, strip
client-supplied forwarded headers, and must not cache authenticated HTML/BFF
responses. The app does not derive issuer, API URL, owner or redirect origin
from forwarded headers. TLS certificates/private keys belong to the proxy,
not the repository or images.

## Build

Requirements: .NET 10, Node 24, pnpm 11.19.0, PostgreSQL 17, Docker for the
isolated test harness. From repository root:

```sh
dotnet restore MedResearch.slnx
dotnet build MedResearch.slnx -c Release --no-restore
dotnet publish src/MedResearch.Api -c Release -o artifacts/api
cd frontend
corepack enable
corepack prepare pnpm@11.19.0 --activate
pnpm install --frozen-lockfile
pnpm security:test
pnpm security:audit
pnpm lint
pnpm typecheck
pnpm test
pnpm build
```

The Next standalone directory is `frontend/apps/web/.next/standalone`; copy
`.next/static` into its `apps/web/.next/static` before running the minimal server.
No static export is supported. Docker packaging performs that copy, keeps only
traced runtime files, runs as the Node user and accepts configuration at runtime:

```sh
docker build -f frontend/apps/web/Dockerfile -t medresearch-web:rc .
docker build -f src/MedResearch.Api/Dockerfile -t medresearch-api:rc .
```

`.dockerignore` excludes env files, node_modules, build caches and diagnostics.
Do not pass secrets as build arguments. Base images use supported major tags;
pin reviewed digests in the deployment environment and scan those images too.

## Runtime Configuration

Inject secrets using the platform's secret manager/restricted runtime env file,
not checked-in templates, shell history, NEXT_PUBLIC variables or Docker args.
Inspect permissions and prevent environment dumps in diagnostics.

Next required: `NODE_ENV=production`, `WEB_AUTH_MODE=Oidc`, HTTPS
`WEB_AUTH_ORIGIN`, origin-only `MEDRESEARCH_API_INTERNAL_URL`, `OIDC_ISSUER`,
`OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`, a distinct `OIDC_API_AUDIENCE`,
`OIDC_SCOPE` containing openid without offline_access, and a random
`WEB_SESSION_SECRET` of at least 32 characters. Optional `OIDC_RESOURCE` and
`OIDC_AUTHORIZATION_AUDIENCE` depend on the IdP; do not invent either.
`WEB_AUTH_TIMEOUT_SECONDS` defaults to 10 (1..30),
`WEB_SESSION_MAX_AGE_SECONDS` to 3600 (60..3600); access-token expiry caps the
actual session lifetime. Missing/unsafe configuration yields unavailable auth,
no session and protected-route 503, not a DevelopmentLocal bypass.

API required: `ASPNETCORE_ENVIRONMENT=Production`,
`Authentication__Mode=JwtBearer`, HTTPS `Authentication__Authority`,
`Authentication__Audience` matching the access-token audience,
`ConnectionStrings__MedResearch` with deployment PostgreSQL credentials.
`ASPNETCORE_URLS` binds only to a private listener. Unsafe authority URLs and
DevelopmentLocal in Production fail startup with a bounded diagnostic.

The IdP must support authorization code/S256 PKCE, client_secret_post,
RS256 ID tokens, and a separate asymmetric audience-scoped JWT access token.
Register only the exact HTTPS `/api/auth/callback` URL. API ownership comes
from the verified `sub`; the browser supplies no authoritative identity header.
No OpenAI/PubMed key is needed for health/auth/read-only startup. Keep
`ResearchProcessing__Enabled=false` for deployment smoke without live research.
Enabling the worker requires separately acknowledged scientific-provider
configuration and opt-in live validation; this milestone does not perform it.

## Migrate, Start, Inspect

Start PostgreSQL with a durable managed volume and wait for readiness. Apply
committed migrations once before exposing the service, using an isolated
migration identity and a backup/roll-forward plan:

```sh
dotnet tool restore
dotnet ef database update --project src/MedResearch.Infrastructure --startup-project src/MedResearch.Api -c Release
```

Supply Production JWT/database env to that command too; do not print its
connection string. `Database__ApplyMigrationsOnStartup=false` is recommended
for deployed replicas; `true` is only the controlled smoke/development approach.
No historic migration is modified by F18.

Run `dotnet artifacts/api/MedResearch.Api.dll` and the Next standalone
`node apps/web/server.js` from its output root with PORT/HOSTNAME set. Then
start the HTTPS proxy. Use bounded readiness polling, not an assumed sleep.

API `/health/live` checks process liveness; `/health/ready` checks PostgreSQL.
Neither invokes scientific providers. `/health` remains the aggregate alias.
Inspect readiness internally; do not use protected dashboard redirects as a
database health signal. Anonymous `/api/research` must return 401. Verify
`/api/auth/session` reports an anonymous configured session before sign-in,
then complete login, create/read a queued run, test a second user's 404, logout
and confirm 401. Runtime private routes send no-store security headers.

Logs should include safe operation/category, ResearchRun/worker/lease IDs;
never cookies, authorization codes, JWTs, session/client secrets, connection
strings or source bodies. API exception responses are bounded. Database outage
must produce readiness 503; upstream API outage must produce BFF 503 and a
visible error, not empty history. Stop traffic first, gracefully terminate
Next/API, retain PostgreSQL data, and expire sessions if rotating the shared
cookie secret. No global revocation/refresh/global IdP logout is implemented.

## Deterministic Verification

On Linux with Docker and libnss3-tools, install Playwright Chromium, build the
production web app, then from repository root:

```sh
dotnet run --project tests/MedResearch.WebReleaseTests --configuration Release
```

This opt-in executable is outside normal solution tests. It owns a fresh
Testcontainers PostgreSQL, applies all migrations, seeds through existing fake
scientific providers/stores, starts a real Production JWT API process, a Next
standalone server, HTTPS proxy and synthetic issuer. Its temporary CA is trusted
through Chromium's current NSS location, Node's extra CA and .NET's certificate
file. OpenSSL generates a separate CA and SAN/serverAuth leaf; TLS verification is
not disabled. Ports 3440..3443 must be free. Scientific fixtures are isolated,
not production data; 97 extra discoveries exercise 100-study read-model scale,
not generated evidence/report claims. Cleanup stops only fixture processes and
its container. No live OpenAI/PubMed/Europe PMC request is made.

CI `web-release` runs this plus image builds. Required backend CI still runs
the complete strict PostgreSQL suite, fresh migrations and EF model check.
Frontend CI runs audits, canonical SDK generation/diff, unit and browser tests.
The older F17 issuer-only browser suite still bypasses validation of its own
test certificate; it is not the evidence for F18's trusted-HTTPS claim.

## Contract Maintenance

The canonical OpenAPI file is captured from actual ASP.NET `/openapi/v1.json`
in a controlled Development test host; the document is not publicly exposed
in Production. Only runtime `servers` is excluded, object keys are sorted.
Paths/security/schema/required/nullability/enums are compared without exclusions.

PowerShell regeneration:

```powershell
$env:MEDRESEARCH_UPDATE_OPENAPI = 'true'
dotnet test tests/MedResearch.IntegrationTests --filter FullyQualifiedName~ActualBackendDocument_MatchesCanonicalSnapshot
Remove-Item Env:MEDRESEARCH_UPDATE_OPENAPI
pnpm --dir frontend api:generate
git diff -- frontend/packages/api
```

Do not enable regeneration in CI. Review the actual wire change, preserve Zod
negative controls/refinements and rerun the drift gate. Do not manually edit
generated TypeScript. Standard ASP.NET health checks are plain text and are
not invented as hand-written OpenAPI schemas.
