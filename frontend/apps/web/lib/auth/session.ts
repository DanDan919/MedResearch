import "server-only";
import { getIronSession, unsealData, webCookies, type SessionOptions } from "iron-session";
import { z } from "zod";
import type { AuthConfig } from "./config";
import type { PublicSession } from "./types";

export type OidcConfig = Extract<AuthConfig, { mode: "oidc" }>;
const privateSessionSchema = z.object({
  version: z.literal(1), issuer: z.string(), subject: z.string().trim().min(1).max(200),
  sessionId: z.uuid(), displayName: z.string().max(100), expiresAt: z.number().finite(),
  accessToken: z.string().min(1).max(3000)
}).strict();
export type PrivateSession = z.infer<typeof privateSessionSchema>;
export type LoginFlow = { verifier: string; state: string; nonce: string; returnTo: string; expiresAt: number };

export function sessionOptions(config: OidcConfig, flow = false, ttl = config.sessionMaxAgeSeconds): SessionOptions {
  const secure = new URL(config.webOrigin).protocol === "https:";
  return {
    password: config.sessionSecret, cookieName: `${secure ? "__Host-" : ""}medresearch-${flow ? "flow" : "session"}`,
    ttl: flow ? 300 : ttl,
    cookieOptions: { secure, httpOnly: true, sameSite: "lax", path: "/", maxAge: flow ? 300 : ttl }
  };
}

export async function readPrivateSession(cookie: string | undefined, config: OidcConfig, now = Date.now()): Promise<PrivateSession | null> {
  if (!cookie || cookie.length > 4096) return null;
  const value = await unsealData<unknown>(cookie, { password: config.sessionSecret, ttl: config.sessionMaxAgeSeconds });
  const parsed = privateSessionSchema.safeParse(value);
  return parsed.success && parsed.data.issuer === config.issuer && parsed.data.expiresAt > now ? parsed.data : null;
}

export function publicSession(config: AuthConfig, session: PrivateSession | null): PublicSession {
  if (config.mode === "development-local") return {
    authenticated: true, mode: config.mode, sessionId: "development-local", displayName: "Local development", expiresAt: null
  };
  return { authenticated: session !== null && config.mode === "oidc", mode: config.mode,
    sessionId: session?.sessionId ?? null, displayName: session?.displayName ?? null, expiresAt: session?.expiresAt ?? null };
}

export async function savePrivateSession(request: Request, response: Response, config: OidcConfig, data: PrivateSession): Promise<void> {
  const ttl = Math.max(1, Math.floor((data.expiresAt - Date.now()) / 1000));
  const session = await getIronSession<PrivateSession>(webCookies(request, response), sessionOptions(config, false, ttl));
  for (const key of Object.keys(session)) delete session[key as keyof PrivateSession];
  Object.assign(session, privateSessionSchema.parse(data));
  await session.save();
}

export async function clearSession(request: Request, response: Response, config: OidcConfig, flow = false): Promise<void> {
  const session = await getIronSession(webCookies(request, response), sessionOptions(config, flow));
  session.destroy();
}

export async function loginFlowSession(request: Request, response: Response, config: OidcConfig) {
  return getIronSession<LoginFlow>(webCookies(request, response), sessionOptions(config, true));
}
