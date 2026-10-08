import "server-only";
import { randomUUID } from "node:crypto";
import { readAuthConfig, trustedRequest, type AuthConfig } from "./config";
import { BodyLimitExceeded, privateHeaders, problem, readBoundedBody } from "./http";
import { AccessTokenFailure, validateAccessToken } from "./oidc";
import { requestCookie } from "./routes";
import { clearSession, readPrivateSession, sessionOptions, type PrivateSession } from "./session";

const id = "[a-f\\d]{8}-[a-f\\d]{4}-[a-f\\d]{4}-[a-f\\d]{4}-[a-f\\d]{12}";
const researchPath = new RegExp(`^/api/research(?:/${id}(?:/(?:progress|report|quantitative|provenance))?)?$`, "i");
const statuses = new Set(["Queued", "Planning", "Searching", "Extracting", "Evaluating", "Synthesizing", "Completed", "Failed", "Cancelled"]);

export function allowedUpstreamPath(url: URL, method: string): string | null {
  if (!url.pathname.startsWith("/api/backend/")) return null;
  const path = url.pathname.slice("/api/backend".length);
  if (path === "/health/ready" && method === "GET" && !url.search) return path;
  if (!researchPath.test(path) || (method !== "GET" && !(method === "POST" && path === "/api/research"))) return null;
  if (url.search && (path !== "/api/research" || method !== "GET")) return null;
  const seen = new Set<string>();
  for (const [key, value] of url.searchParams) {
    if (seen.has(key) || !["page", "pageSize", "status"].includes(key)) return null;
    seen.add(key);
    if (key === "status" ? !statuses.has(value) : !/^\d{1,6}$/.test(value) || Number(value) < 1 || (key === "pageSize" && Number(value) > 100)) return null;
  }
  return path + url.search;
}

type BffDependencies = {
  config?: AuthConfig;
  fetch?: typeof fetch;
  readSession?: typeof readPrivateSession;
  verifyToken?: typeof validateAccessToken;
};

export async function forwardBackend(request: Request, dependencies: BffDependencies = {}): Promise<Response> {
  const config = dependencies.config ?? readAuthConfig();
  if (config.mode === "unavailable") return problem(503, "Authentication is not configured", "auth-unavailable");
  const upstreamPath = allowedUpstreamPath(new URL(request.url), request.method);
  if (!upstreamPath) return problem(404, "Endpoint not found", "path-rejected");
  if (!trustedRequest(request, config, request.method !== "GET")) return problem(403, "Request origin is not allowed", "origin-rejected");
  const health = upstreamPath === "/health/ready";
  let session: PrivateSession | null = null;
  if (!health && config.mode === "oidc") {
    session = await (dependencies.readSession ?? readPrivateSession)(requestCookie(request, sessionOptions(config).cookieName), config);
    if (!session) return problem(401, "Sign in required", "session-expired");
    try { await (dependencies.verifyToken ?? validateAccessToken)(config, session.accessToken, session.subject); }
    catch (error) {
      if (error instanceof AccessTokenFailure && error.category === "provider-unavailable") return problem(503, "Identity service unavailable", "provider-unavailable");
      const response = problem(401, "Sign in required", "invalid-session");
      await clearSession(request, response, config);
      return response;
    }
  }
  const signal = AbortSignal.any([request.signal, AbortSignal.timeout((config.mode === "oidc" ? config.timeoutSeconds : 10) * 1000)]);
  const headers: Record<string, string> = { Accept: health ? "text/plain" : "application/json", "X-Correlation-Id": randomUUID() };
  if (session) headers.Authorization = `Bearer ${session.accessToken}`;
  let body: string | undefined;
  if (request.method === "POST") {
    if (request.headers.get("content-type")?.split(";")[0].trim() !== "application/json") return problem(415, "JSON is required", "unsupported-media-type");
    try {
      const incoming = JSON.parse(new TextDecoder().decode(await readBoundedBody(request, 16_384, signal))) as Record<string, unknown>;
      if (!incoming || Array.isArray(incoming) || typeof incoming.question !== "string" || Object.keys(incoming).some(key => key !== "question"))
        return problem(400, "Invalid research request", "invalid-request");
      body = JSON.stringify({ question: incoming.question });
      headers["Content-Type"] = "application/json";
    } catch (error) { return problem(error instanceof BodyLimitExceeded ? 413 : 400, "Invalid research request", "invalid-request"); }
  }
  try {
    const upstream = await (dependencies.fetch ?? fetch)(config.apiBaseUrl + upstreamPath, {
      method: request.method, headers, body, signal, cache: "no-store", redirect: "manual"
    });
    if (upstream.status >= 300 && upstream.status < 400) {
      await upstream.body?.cancel();
      return problem(502, "API response unavailable", "upstream-redirect-rejected");
    }
    if (!upstream.ok) {
      await upstream.body?.cancel();
      const title = upstream.status === 401 ? "Sign in required" : upstream.status === 404 ? "Resource not found" :
        upstream.status === 409 ? "Report is not ready" : upstream.status === 429 ? "Too many requests" : "API request could not be completed";
      const response = problem(upstream.status, title, upstream.status === 401 ? "backend-rejected" : "upstream-error");
      if (upstream.status === 401 && config.mode === "oidc") await clearSession(request, response, config);
      const retryAfter = upstream.headers.get("retry-after");
      if (upstream.status === 429 && retryAfter && /^\d{1,5}$/.test(retryAfter)) response.headers.set("Retry-After", retryAfter);
      return response;
    }
    const bytes = await readBoundedBody(upstream, 10_000_000, signal);
    const response = new Response(bytes, { status: upstream.status, headers: { ...privateHeaders, "Content-Type": health ? "text/plain" : "application/json" } });
    const location = upstream.headers.get("location");
    if (location) {
      try {
        const target = new URL(location, config.apiBaseUrl);
        if (target.origin === new URL(config.apiBaseUrl).origin && researchPath.test(target.pathname) && !target.search && !target.hash)
          response.headers.set("Location", `/api/backend${target.pathname}`);
      } catch { /* Invalid upstream Location is omitted, not a new failed creation. */ }
    }
    return response;
  } catch {
    console.warn("WebBffFailure", { category: "api-unavailable" });
    return problem(503, "API unavailable", "api-unavailable");
  }
}
