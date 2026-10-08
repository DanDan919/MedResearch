import { NextResponse, type NextRequest } from "next/server";
import { readAuthConfig, safeReturnPath, trustedRequest } from "./lib/auth/config";
import { privateHeaders } from "./lib/auth/http";
import { readPrivateSession, sessionOptions } from "./lib/auth/session";

export async function proxy(request: NextRequest) {
  const config = readAuthConfig();
  if (config.mode !== "unavailable" && !trustedRequest(request, config)) {
    return NextResponse.json({ title: "Request origin is not allowed", status: 403 }, { status: 403, headers: privateHeaders });
  }
  const path = request.nextUrl.pathname;
  const isWorkspace = path === "/" || /^\/(research|studies|settings)(\/|$)/.test(path);
  if (isWorkspace && config.mode !== "development-local") {
    const session = config.mode === "oidc" ? await readPrivateSession(request.cookies.get(sessionOptions(config).cookieName)?.value, config) : null;
    if (!session) {
      const target = `/login?returnTo=${encodeURIComponent(safeReturnPath(path + request.nextUrl.search))}`;
      const response = new NextResponse(null, { status: 307, headers: {
        Location: config.mode === "oidc" ? config.webOrigin + target : target
      } });
      for (const [key, value] of Object.entries(privateHeaders)) response.headers.set(key, value);
      return response;
    }
  }
  const response = NextResponse.next();
  for (const [key, value] of Object.entries(privateHeaders)) response.headers.set(key, value);
  return response;
}

export const config = { matcher: ["/", "/research/:path*", "/studies/:path*", "/settings/:path*", "/login"] };
