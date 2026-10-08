# Production Web Authentication

## Topology and Trust

Browser -> HTTPS Next.js server -> fixed internal ASP.NET API -> PostgreSQL.
Next.js and ASP.NET independently validate the issuer's access JWT. Next.js also
performs OIDC discovery/token/JWKS requests over HTTPS. ASP.NET uses its own
discovery/JWKS cache/rotation. The API accepts one configured issuer; ownership
uses its bounded opaque `sub`, not email/name or browser owner headers.

Use a server runtime, not Next.js static export. The current Compose file is
development API/PostgreSQL only; it does not deploy Next.js or an identity provider.
Terminate HTTPS at a trusted reverse proxy and preserve the exact public Host.
Do not expose an alternative Next.js Host or trust arbitrary forwarded headers.
Keep the API private to the BFF where practical; its JWT policy remains mandatory.

## Register a Real OIDC Client

1. Register a confidential authorization-code client at your chosen trusted IdP.
2. Require S256 PKCE; enable `client_secret_post` token endpoint authentication
   and RS256-signed ID tokens. Discovery endpoints/JWKS must use HTTPS.
3. Register the exact callback `https://<your-web-origin>/api/auth/callback`.
   Do not use wildcard callbacks. The configured origin contains no path/query.
4. Register an API resource/audience distinct from the web client ID. The IdP
   must issue a separate signed JWT access token (RS256/PS256/ES256), with `sub`,
   `exp`, the expected `iss` and API `aud`. Opaque tokens are not supported.
5. Grant only required API scopes and `openid`; optionally request `profile`.
   Set resource/audience request parameters only as supported by this provider.
   Do not request `offline_access`: this release deliberately reauthenticates.
6. Supply real credentials through deployment secret management, never Git,
   NEXT_PUBLIC variables, client components or a checked-in .env file.

If the provider returns only an ID token, an opaque access token, incompatible
audience, client_secret_basic-only authentication or unsupported ID signing
algorithm, this implementation fails rather than substituting an ID token.
Generic OIDC compatibility is explicit, not a claim that every vendor defaults
will work. Preserve the exact issuer string, including its trailing slash.

## Server Environment

| Variable | Requirement |
|---|---|
| `WEB_AUTH_MODE` | `Oidc` in production; missing defaults to OIDC, not a bypass |
| `WEB_AUTH_ORIGIN` | Required HTTPS public origin |
| `MEDRESEARCH_API_INTERNAL_URL` | Required fixed http/https API origin; no path, credentials or query |
| `OIDC_ISSUER` | Required exact HTTPS discovery issuer |
| `OIDC_CLIENT_ID` | Required registered confidential web client |
| `OIDC_CLIENT_SECRET` | Required server-only client secret |
| `OIDC_API_AUDIENCE` | Required, distinct from client ID; align API Audience |
| `OIDC_SCOPE` | Required includes openid and configured API scopes; offline_access rejected |
| `OIDC_RESOURCE` | Optional RFC 8707 resource if supported |
| `OIDC_AUTHORIZATION_AUDIENCE` | Optional provider-specific audience parameter if supported |
| `WEB_SESSION_SECRET` | Required random secret, at least 32 characters |
| `WEB_SESSION_MAX_AGE_SECONDS` | Default 3600, bounds 60..3600; token may expire sooner |
| `WEB_AUTH_TIMEOUT_SECONDS` | Default 10, bounds 1..30; bounded network/body reads |

API environment: `Authentication__Mode=JwtBearer`,
`Authentication__Authority=<matching issuer>`,
`Authentication__Audience=<same API audience>`, plus production PostgreSQL settings.
The API's unchanged framework clock skew is five minutes. The BFF independently
requires unexpired tokens with zero skew. Normal health does not require OIDC
network calls or scientific-provider credentials.

Next.js loads its environment at the web app process, not from root Compose
automatically. For local-only development supply values in an ignored
`frontend/apps/web/.env.local` or process environment. Use
`WEB_AUTH_MODE=DevelopmentLocal`, `WEB_AUTH_ORIGIN=http://127.0.0.1:3000`,
`MEDRESEARCH_API_INTERNAL_URL=http://localhost:8080` with `pnpm dev` and an API in
Development/DevelopmentLocal. This web mode is rejected in Production/test.
Do not run that configuration on an exposed production server.

## Routes and Session

Public auth routes: `/login`, POST `/api/auth/login`, GET `/api/auth/callback`,
GET `/api/auth/session`, POST `/api/auth/logout`; static assets remain public.
Login/session availability are not scientific authorization. Proxy protects
dashboard, research/new/history/details/report/evidence/quantitative, studies
and settings; the BFF authorizes independently.

Login/logout use same-origin fetch, exact Origin/Host and safe Fetch Metadata,
then navigation. JS is required. The no-referrer policy is retained; native form
navigation sends Origin=null and is deliberately rejected, not exempted from CSRF.
The callback is cross-site GET with one-use code/PKCE/state/nonce and a five-minute
sealed flow cookie, not an Origin-based mutation. Return paths are bounded
allowlisted workspace routes, never arbitrary external URLs.

Cookies: `__Host-medresearch-session` and `__Host-medresearch-flow`, Secure,
HttpOnly, SameSite=Lax, Path=/, no Domain. Private session holds only sealed
issuer/sub/session ID/display/expiry/access token. Public session exposes only
authenticated/mode/opaque session ID/display name/expiry. Tokens do not enter
React props or browser storage. Session expires at min(access expiry, configured
maximum); no refresh token is retained, even if the provider returns one.
Cookie sealing is limited to 4KB, access tokens to 3000 characters; oversized
provider tokens may fail closed. Provider JWT size is a deployment compatibility check.

Logout clears app/flow cookies and scientific QueryClients. It does not revoke
all access tokens, destroy the IdP login session or erase stolen cookie copies.
Account selection is requested with `prompt=select_account`; actual provider UX
must be verified. Replacing WEB_SESSION_SECRET invalidates existing cookies;
graceful multi-key rotation is not exposed. Another suspended tab may retain an
already-rendered view until BroadcastChannel/focus/20-second polling; future data
requests still require valid session/JWT. bfcache restoration rechecks session.

## BFF Contract

The existing SDK uses browser base `/api/backend`, no browser getAccessToken hook.
GET `/api/backend/health/ready` is limited public readiness. Research forwarding:
GET `/api/backend/api/research` with bounded page/pageSize/closed status; GET
`/api/backend/api/research/{uuid}` and progress/report/quantitative/provenance;
POST `/api/backend/api/research` with only JSON question. No arbitrary destination
or method. Browser Authorization/cookies/identity/forwarded headers are ignored.
Only the server's access JWT is attached. Upstream redirects are not followed.
Requests are deadline-bound; create input <=16KB, successful response <=10MB.
Auth provider responses <=1MB. Error bodies are discarded/sanitized, not logged.
Responses use private/no-store. Safe relative creation Location is rewritten.

401 distinguishes sign-in/session expiry/backend rejection; 503 separates IdP
and API unavailability. 403/404/409/429/500 statuses remain meaningful without
backend stack/ownership disclosure. Host/Origin checks supplement SameSite;
no browser CORS credential access to the API is necessary. API CORS remains
allowlisted, never an authorization replacement. Full script CSP/XSS prevention
and authenticated resource quotas are separate work.

## Verification and Operator Checklist

`pnpm test` verifies configuration/session/BFF/cache boundaries deterministically.
`pnpm test:e2e` verifies scientific views in DevelopmentLocal. After `pnpm build`,
`pnpm test:auth-e2e` runs production Next.js against an ephemeral HTTPS issuer and
synthetic API, with real code exchange, PKCE verification and signed tokens.
The fixture is not imported by production routes. Only the test process trusts
its generated CA; production TLS validation is not disabled. No external IdP
credentials or live scientific calls occur. Independently, .NET tests use the
actual JWT handler and PostgreSQL/Testcontainers owner stores; strict CI fails
on required DB skips. These are complementary tests, not a real deployment.

- Unconfigured: login accurately unavailable, research BFF fails closed.
- Configured: operator has set real issuer/client/API/cookie/TLS settings.
- Deterministically tested: synthetic CI passes; not external IdP verification.
- Real IdP verified: operator actually signs in with two accounts, creates/reads
  owned runs and checks foreign run/report/quantitative/provenance rejection.
- Verify logout, expiry, account selection, dormant tab/back navigation and no
  browser-visible token; check key rollover and issuer/audience/subject alignment.
- Disable DevelopmentLocal, keep secrets out of logs, plan cookie-secret rotation
  and production migrations separately. Do not equate readiness with IdP uptime.

Real IdP verification is NOT RUN for F17. No credentials were invented.
