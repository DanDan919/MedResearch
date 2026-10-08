import { forwardBackend } from "../../../../lib/auth/bff";
export const runtime = "nodejs";
export const dynamic = "force-dynamic";
export const GET = (request: Request) => forwardBackend(request);
export const POST = (request: Request) => forwardBackend(request);
