import { callback } from "../../../../lib/auth/routes";
export const runtime = "nodejs";
export const dynamic = "force-dynamic";
export const GET = (request: Request) => callback(request);
