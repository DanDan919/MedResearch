import { login } from "../../../../lib/auth/routes";
export const runtime = "nodejs";
export const dynamic = "force-dynamic";
export const POST = (request: Request) => login(request);
