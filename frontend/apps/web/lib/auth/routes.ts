import "server-only";
import { randomUUID } from "node:crypto";
import { z } from "zod";
import { readAuthConfig, safeReturnPath, trustedRequest, type AuthConfig } from "./config";
import { privateHeaders, privateRedirect, problem } from "./http";
import { finishAuthorization, startAuthorization, validateAccessToken, AccessTokenFailure } from "./oidc";
import { clearSession, loginFlowSession, publicSession, readPrivateSession, savePrivateSession, sessionOptions } from "./session";

export function requestCookie(request: Request, name: string): string | undefined {
  return request.headers.get("cookie")?.split(";").map(part => part.trim()).find(part => part.startsWith(`${name}=`))?.slice(name.length + 1);
}

export async function sessionStatus(request: Request, config: AuthConfig = readAuthConfig()): Promise<Response> {
  if (config.mode === "unavailable") return Response.json(publicSession(config, null), { headers: privateHeaders });
  if (!trustedRequest(request, config)) return problem(403, "Request origin is not allowed", "origin-rejected");
  if (config.mode === "development-local") return Response.json(publicSession(config, null), { headers: privateHeaders });
  const session = await readPrivateSession(requestCookie(request, sessionOptions(config).cookieName), config);
  if (session) {
    try { await validateAccessToken(config, session.accessToken, session.subject); }
    catch (error) {
      if (error instanceof AccessTokenFailure && error.category === "provider-unavailable") return problem(503, "Identity service unavailable", "provider-unavailable");
      const response = Response.json(publicSession(config, null), { headers: privateHeaders });
      await clearSession(request, response, config);
      return response;
    }
  }
  return Response.json(publicSession(config, session), { headers: privateHeaders });
}

export async function login(request: Request, config: AuthConfig = readAuthConfig()): Promise<Response> {
  if (config.mode !== "oidc") return problem(503, "Authentication is not configured", "auth-unavailable");
  if (!trustedRequest(request, config, true)) return problem(403, "Request origin is not allowed", "origin-rejected");
  if (request.url.length > 4096) return problem(400, "Invalid sign-in request", "invalid-request");
  const jsonResponse = request.headers.get("accept") === "application/json";
  try {
    const authorization = await startAuthorization(config);
    const response = jsonResponse ? Response.json({ authorizationUrl: authorization.url }, { headers: privateHeaders }) : privateRedirect(authorization.url);
    await clearSession(request, response, config);
    const session = await loginFlowSession(request, response, config);
    Object.assign(session, { verifier: authorization.verifier, state: authorization.state, nonce: authorization.nonce,
      returnTo: safeReturnPath(new URL(request.url).searchParams.get("returnTo")), expiresAt: Date.now() + 300_000 });
    await session.save();
    return response;
  } catch {
    console.warn("WebAuthFailure", { operation: "login", category: "provider-unavailable" });
    return jsonResponse ? problem(503, "Identity service unavailable", "provider-unavailable") : privateRedirect(`${config.webOrigin}/login?reason=provider-unavailable`);
  }
}

const flowSchema = z.object({ verifier: z.string().min(32).max(256), state: z.string().min(32).max(256),
  nonce: z.string().min(32).max(256), returnTo: z.string().max(1024), expiresAt: z.number().finite() });

export async function callback(request: Request, config: AuthConfig = readAuthConfig()): Promise<Response> {
  if (config.mode !== "oidc") return problem(503, "Authentication is not configured", "auth-unavailable");
  if (!trustedRequest(request, config) || request.url.length > 4096) return problem(403, "Callback rejected", "callback-rejected");
  const response = privateRedirect(`${config.webOrigin}/login?reason=authentication-failed`);
  const saved = await loginFlowSession(request, response, config);
  const flow = flowSchema.safeParse(saved);
  saved.destroy();
  try {
    if (!flow.success || flow.data.expiresAt <= Date.now()) throw new Error("Expired authorization transaction");
    const incoming = new URL(request.url);
    const callbackUrl = new URL(`${config.webOrigin}/api/auth/callback${incoming.search}`);
    const authenticated = await finishAuthorization(config, callbackUrl, flow.data);
    await savePrivateSession(request, response, config, { version: 1, issuer: config.issuer, sessionId: randomUUID(), ...authenticated });
    response.headers.set("Location", config.webOrigin + safeReturnPath(flow.data.returnTo));
    return response;
  } catch {
    await clearSession(request, response, config);
    console.warn("WebAuthFailure", { operation: "callback", category: "authentication-failed" });
    return response;
  }
}

export async function logout(request: Request, config: AuthConfig = readAuthConfig()): Promise<Response> {
  if (config.mode !== "oidc") return problem(503, "Authentication is not configured", "auth-unavailable");
  if (!trustedRequest(request, config, true)) return problem(403, "Request origin is not allowed", "origin-rejected");
  const response = request.headers.get("accept") === "application/json" ? new Response(null, { status: 204, headers: privateHeaders }) :
    privateRedirect(`${config.webOrigin}/login?reason=signed-out`);
  await clearSession(request, response, config);
  await clearSession(request, response, config, true);
  return response;
}
