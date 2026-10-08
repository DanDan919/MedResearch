import "server-only";

export const privateHeaders = { "Cache-Control": "private, no-store, max-age=0", "Referrer-Policy": "no-referrer", "Vary": "Cookie" };

export class BodyLimitExceeded extends Error {}

export function problem(status: number, title: string, code: string): Response {
  return Response.json({ type: "about:blank", title, status, code }, { status, headers: privateHeaders });
}

export function privateRedirect(url: string, status = 303): Response {
  return new Response(null, { status, headers: { ...privateHeaders, Location: url } });
}

export async function readBoundedBody(response: Response | Request, maxBytes: number, signal?: AbortSignal): Promise<Uint8Array<ArrayBuffer>> {
  const length = Number(response.headers.get("content-length"));
  if (length > maxBytes) {
    await response.body?.cancel().catch(() => undefined);
    throw new BodyLimitExceeded("Body too large");
  }
  const reader = response.body?.getReader();
  if (!reader) return new Uint8Array();
  const chunks: Uint8Array[] = [];
  let size = 0;
  const abort = () => { void reader.cancel().catch(() => undefined); };
  signal?.addEventListener("abort", abort, { once: true });
  try {
    while (true) {
      signal?.throwIfAborted();
      const part = await reader.read();
      signal?.throwIfAborted();
      if (part.done) break;
      size += part.value.byteLength;
      if (size > maxBytes) throw new BodyLimitExceeded("Body too large");
      chunks.push(part.value);
    }
    const bytes = new Uint8Array(size);
    let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    return bytes;
  } finally {
    signal?.removeEventListener("abort", abort);
    await reader.cancel().catch(() => undefined);
    reader.releaseLock();
  }
}
