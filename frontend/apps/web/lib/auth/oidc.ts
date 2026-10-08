import "server-only";
import * as oidc from "openid-client";
import { createRemoteJWKSet, customFetch as jwksFetch, errors, jwtVerify } from "jose";
import { readBoundedBody } from "./http";
import type { OidcConfig } from "./session";

export class AccessTokenFailure extends Error {
  constructor(public readonly category: "invalid-token" | "provider-unavailable") { super(category); }
}

let discoveryCache: { key: string; promise: Promise<oidc.Configuration> } | undefined;
const keySets = new WeakMap<oidc.Configuration, ReturnType<typeof createRemoteJWKSet>>();

function boundedFetch(timeout: number): typeof fetch {
  return async (input, init) => {
    const address = input instanceof Request ? input.url : String(input);
    if (new URL(address).protocol !== "https:") throw new Error("OIDC requires HTTPS");
    const signal = AbortSignal.any([...(init?.signal ? [init.signal] : []), AbortSignal.timeout(timeout * 1000)]);
    const response = await fetch(input, { ...init, signal, cache: "no-store", redirect: "manual" });
    if (response.status >= 300 && response.status < 400) {
      await response.body?.cancel();
      throw new Error("OIDC redirects are not accepted");
    }
    const bytes = await readBoundedBody(response, 1_000_000, signal);
    return new Response(bytes, { status: response.status, headers: response.headers });
  };
}

export async function oidcConfiguration(config: OidcConfig): Promise<oidc.Configuration> {
  const key = `${config.issuer}|${config.clientId}|${config.clientSecret}`;
  if (discoveryCache?.key !== key) {
    discoveryCache = { key, promise: oidc.discovery(new URL(config.issuer), config.clientId,
      { client_secret: config.clientSecret, id_token_signed_response_alg: "RS256" }, oidc.ClientSecretPost(config.clientSecret),
      { timeout: config.timeoutSeconds, [oidc.customFetch]: (address, options) => boundedFetch(config.timeoutSeconds)(address, {
        ...options, body: options.body instanceof Uint8Array ? new Uint8Array(options.body).buffer : options.body
      }), execute: [oidc.enableNonRepudiationChecks] }) };
  }
  try {
    const discovered = await discoveryCache.promise;
    const metadata = discovered.serverMetadata();
    for (const endpoint of [metadata.authorization_endpoint, metadata.token_endpoint, metadata.jwks_uri]) {
      if (!endpoint || new URL(endpoint).protocol !== "https:" || new URL(endpoint).username || new URL(endpoint).password)
        throw new Error("Invalid OIDC endpoint");
    }
    return discovered;
  } catch (error) { discoveryCache = undefined; throw error; }
}

export async function validateAccessToken(config: OidcConfig, token: string, expectedSubject: string, idToken?: string): Promise<{ subject: string; expiresAt: number }> {
  if (!token || token.length > 3000 || token === idToken) throw new AccessTokenFailure("invalid-token");
  try {
    const discovered = await oidcConfiguration(config);
    let keys = keySets.get(discovered);
    if (!keys) {
      keys = createRemoteJWKSet(new URL(discovered.serverMetadata().jwks_uri!), {
        timeoutDuration: config.timeoutSeconds * 1000, [jwksFetch]: boundedFetch(config.timeoutSeconds)
      });
      keySets.set(discovered, keys);
    }
    const { payload } = await jwtVerify(token, keys, {
      issuer: config.issuer, audience: config.audience, requiredClaims: ["exp", "sub"],
      algorithms: ["RS256", "PS256", "ES256"], clockTolerance: 0
    });
    const audiences = typeof payload.aud === "string" ? [payload.aud] : payload.aud ?? [];
    if (payload.sub !== expectedSubject || !payload.sub?.trim() || payload.sub.length > 200 || payload.sub.trim() !== payload.sub ||
        audiences.includes(config.clientId) || payload.token_use === "id" || !payload.exp || !Number.isFinite(payload.exp))
      throw new AccessTokenFailure("invalid-token");
    return { subject: payload.sub, expiresAt: payload.exp * 1000 };
  } catch (error) {
    if (error instanceof AccessTokenFailure) throw error;
    if (error instanceof errors.JOSEError && error.code !== "ERR_JWKS_TIMEOUT") throw new AccessTokenFailure("invalid-token");
    throw new AccessTokenFailure("provider-unavailable");
  }
}

export async function startAuthorization(config: OidcConfig) {
  const discovered = await oidcConfiguration(config);
  const verifier = oidc.randomPKCECodeVerifier();
  const state = oidc.randomState();
  const nonce = oidc.randomNonce();
  const parameters: Record<string, string> = {
    redirect_uri: `${config.webOrigin}/api/auth/callback`, scope: config.scope, response_type: "code",
    response_mode: "query", code_challenge_method: "S256", code_challenge: await oidc.calculatePKCECodeChallenge(verifier),
    state, nonce, prompt: "select_account"
  };
  if (config.resource) parameters.resource = config.resource;
  if (config.authorizationAudience) parameters.audience = config.authorizationAudience;
  return { url: oidc.buildAuthorizationUrl(discovered, parameters).href, verifier, state, nonce };
}

export async function finishAuthorization(config: OidcConfig, callback: URL, flow: { verifier: string; state: string; nonce: string }) {
  const discovered = await oidcConfiguration(config);
  const tokens = await oidc.authorizationCodeGrant(discovered, callback, {
    pkceCodeVerifier: flow.verifier, expectedState: flow.state, expectedNonce: flow.nonce, idTokenExpected: true
  }, config.resource ? { resource: config.resource } : undefined);
  const claims = tokens.claims();
  if (!claims?.sub || tokens.token_type?.toLowerCase() !== "bearer") throw new AccessTokenFailure("invalid-token");
  const access = await validateAccessToken(config, tokens.access_token, claims.sub, tokens.id_token);
  return { accessToken: tokens.access_token, subject: access.subject,
    displayName: typeof claims.name === "string" ? claims.name.slice(0, 100) : "Signed in",
    expiresAt: Math.min(access.expiresAt, Date.now() + config.sessionMaxAgeSeconds * 1000) };
}
