import "server-only";

export type AuthConfig = {
  mode: "oidc";
  webOrigin: string;
  apiBaseUrl: string;
  issuer: string;
  clientId: string;
  clientSecret: string;
  audience: string;
  scope: string;
  resource?: string;
  authorizationAudience?: string;
  sessionSecret: string;
  sessionMaxAgeSeconds: number;
  timeoutSeconds: number;
} | { mode: "development-local"; webOrigin: string; apiBaseUrl: string }
  | { mode: "unavailable" };

function url(value: string | undefined, https: boolean, originOnly = false): string {
  if (!value) throw new Error("Missing configuration");
  const parsed = new URL(value);
  if ((https ? parsed.protocol !== "https:" : !["http:", "https:"].includes(parsed.protocol)) ||
      parsed.username || parsed.password || parsed.search || parsed.hash || parsed.hostname.endsWith(".invalid") ||
      (originOnly && parsed.pathname !== "/")) throw new Error("Invalid URL configuration");
  return originOnly ? parsed.origin : value;
}

function required(value: string | undefined): string {
  if (!value?.trim() || /^(?:replace|change|your)[-_ ]|^</i.test(value)) throw new Error("Missing configuration");
  return value.trim();
}

function integer(value: string | undefined, fallback: number, min: number, max: number): number {
  const parsed = value === undefined ? fallback : Number(value);
  if (!Number.isInteger(parsed) || parsed < min || parsed > max) throw new Error("Invalid bounded configuration");
  return parsed;
}

export function readAuthConfig(env: NodeJS.ProcessEnv = process.env): AuthConfig {
  try {
    const production = env.NODE_ENV === "production";
    if (env.WEB_AUTH_MODE === "DevelopmentLocal") {
      if (env.NODE_ENV !== "development") return { mode: "unavailable" };
      return { mode: "development-local", webOrigin: url(env.WEB_AUTH_ORIGIN ?? "http://127.0.0.1:3000", false, true),
        apiBaseUrl: url(env.MEDRESEARCH_API_INTERNAL_URL ?? "http://localhost:8080", false, true) };
    }
    if (env.WEB_AUTH_MODE && env.WEB_AUTH_MODE !== "Oidc") return { mode: "unavailable" };
    const clientId = required(env.OIDC_CLIENT_ID);
    const audience = required(env.OIDC_API_AUDIENCE);
    const scope = required(env.OIDC_SCOPE);
    if (audience === clientId || !scope.split(/\s+/).includes("openid") || scope.split(/\s+/).includes("offline_access"))
      return { mode: "unavailable" };
    const secret = required(env.WEB_SESSION_SECRET);
    if (secret.length < 32) return { mode: "unavailable" };
    return {
      mode: "oidc", webOrigin: url(env.WEB_AUTH_ORIGIN, production, true),
      apiBaseUrl: url(env.MEDRESEARCH_API_INTERNAL_URL, false, true), issuer: url(env.OIDC_ISSUER, true),
      clientId, clientSecret: required(env.OIDC_CLIENT_SECRET), audience, scope,
      resource: env.OIDC_RESOURCE?.trim() || undefined,
      authorizationAudience: env.OIDC_AUTHORIZATION_AUDIENCE?.trim() || undefined,
      sessionSecret: secret,
      sessionMaxAgeSeconds: integer(env.WEB_SESSION_MAX_AGE_SECONDS, 3600, 60, 3600),
      timeoutSeconds: integer(env.WEB_AUTH_TIMEOUT_SECONDS, 10, 1, 30)
    };
  } catch { return { mode: "unavailable" }; }
}

export function safeReturnPath(value: string | null | undefined): string {
  const unsafe = (text: string) => [...text].some(character => character === "\\" || character.charCodeAt(0) <= 32);
  if (!value || value.length > 1024 || unsafe(value)) return "/";
  try {
    const decoded = decodeURIComponent(value);
    if (unsafe(decoded) || decoded.includes("%") || decoded.startsWith("//")) return "/";
    const path = new URL(value, "https://return.invalid");
    if (path.origin !== "https://return.invalid" || path.hash ||
      !/^\/(?:research(?:\/(?:new|[a-f\d-]{36}(?:\/(?:report|evidence|quantitative))?))?|studies|settings)?$/i.test(path.pathname)) return "/";
    return path.pathname + path.search;
  } catch { return "/"; }
}

export function trustedRequest(request: Request, config: Exclude<AuthConfig, { mode: "unavailable" }>, mutation = false): boolean {
  const origin = new URL(config.webOrigin);
  if (request.headers.get("host") !== origin.host) return false;
  if (mutation && request.headers.get("origin") !== origin.origin) return false;
  const site = request.headers.get("sec-fetch-site");
  return !mutation || site === null || site === "same-origin" || site === "none";
}
