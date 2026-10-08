# ADR-032: Web Authentication Uses OIDC and a Server-Side BFF Boundary

Date: 2026-10-08
Status: Accepted

## Context

F10 independently validates JWTs and scopes scientific reads by the immutable subject from one configured issuer. The web SDK has a bearer hook, but no browser login or token lifecycle. A login screen alone would not close that boundary.

## Decision

Use pinned openid-client 6.8.8 for generic OIDC discovery and authorization-code grant with S256 PKCE, state and nonce; use iron-session 9.0.1 for encrypted/authenticated HttpOnly cookies. Both libraries are MIT licensed and support the Node 24 runtime used here. Next.js 16 App Router route handlers perform the standard library grant, not handwritten token parsing. jose validates the separately returned JWT access token against configured API audience, issuer, signature, expiry and the ID token subject. An ID token is never the API bearer credential.

The short-lived session contains the access token in an encrypted cookie, not a public session DTO, browser storage or React props. Production cookies are host-only, Secure, HttpOnly, SameSite=Lax, Path=/, with a __Host- name. This is stateless encrypted token custody: possession/replay of a stolen cookie remains possible until bounded expiry. No session database or immediate global revocation is claimed. The session expires no later than the access token or configured maximum. Refresh tokens are neither requested nor retained; expired/rejected access requires a new authorization-code flow. This avoids rotation/concurrent-refresh races rather than claiming to solve them.

The browser SDK calls a same-origin Next.js BFF. Only configured MedResearch API origin and explicit existing research/health paths and methods can be forwarded. Browser Authorization, cookies and identity/forwarding headers are not forwarded. The BFF derives bearer solely from the sealed session; upstream redirects are not followed. Authenticated responses are no-store/private; mutations and login/logout require configured Origin and Host plus safe fetch metadata. Redirect targets are bounded internal workspace paths. Session changes discard frontend query state; the API remains the authoritative owner boundary.

Next.js Proxy performs only optimistic cookie/expiry checks for workspace navigation. BFF handlers independently validate session/access-token state near data access. Static assets and login/auth endpoints are public, not research data. Missing/invalid production configuration fails closed with a safe unavailable state. DevelopmentLocal is explicitly development-only. Synthetic issuer, keys and API fixtures are test infrastructure, never a production bypass or built-in issuer.

## Rejected Alternatives

Login/logout UX uses same-origin fetch before navigation while retaining
no-referrer. Native form POST can send Origin=null with that policy and is not
exempted from strict CSRF checks. Browser JS is required for these operations.

- Browser localStorage/sessionStorage tokens expose credentials to JavaScript.
- Homemade JWT issuance/password storage duplicates an identity provider.
- ID token forwarding confuses OIDC client identity with API authorization.
- Trusting Next.js source IP or browser owner headers removes independent JWT enforcement.
- A generic URL proxy introduces SSRF and credentials forwarding.
- Refresh-token storage without a safe rotation/concurrency design expands this milestone unnecessarily.

## Deployment and Verification

Requires a Next.js server runtime, ASP.NET Core API, PostgreSQL and explicitly registered OIDC provider/client. Static export cannot implement this BFF. Browser HTTPS origin/callback and issuer are operator configuration; API authority/audience must align. One issuer is supported; owner identity remains its stable opaque sub, never email. Reverse proxy must preserve the configured Host and provide TLS; secrets remain server-only.

Official sources reviewed before implementation: [openid-client](https://github.com/panva/openid-client), [authorizationCodeGrant](https://github.com/panva/openid-client/blob/main/docs/functions/authorizationCodeGrant.md), [iron-session v9](https://github.com/vvo/iron-session/tree/v9.0.1), [Next.js authentication](https://nextjs.org/docs/app/guides/authentication). Registry versions and Node requirements were checked, not inferred from unpinned latest.

Deterministic tests must exercise synthetic HTTPS OIDC, actual ASP.NET JWT middleware, owner-scoped PostgreSQL reads, BFF attack rejection and production-built browser login/logout/expiry. These do not certify an external IdP deployment, TLS topology, or live scientific providers. No scientific algorithms or schema are changed by this decision.
