type Submission = { fingerprint: string; key: string };
const uuid = /^[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}$/i;
const storageKey = (sessionId: string) => `quick-submission-v1:${sessionId}`;

export async function prepareQuickSubmission(question: string, sessionId: string | null): Promise<Submission> {
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(`quick-create-v1\n${question}`));
  const fingerprint = Array.from(new Uint8Array(hash), b => b.toString(16).padStart(2, "0")).join("");
  if (sessionId) {
    try {
      const saved: Submission | null = JSON.parse(sessionStorage.getItem(storageKey(sessionId)) ?? "null");
      if (saved?.fingerprint === fingerprint && typeof saved.key === "string" && uuid.test(saved.key)
        && saved.key !== "00000000-0000-0000-0000-000000000000") return saved;
    } catch { /* Storage is optional; the mounted form still retains its submission key. */ }
  }
  const submission = { fingerprint, key: crypto.randomUUID() };
  if (sessionId) try { sessionStorage.setItem(storageKey(sessionId), JSON.stringify(submission)); } catch { /* Per-form fallback. */ }
  return submission;
}

export function forgetQuickSubmission(sessionId: string | null, key: string) {
  if (!sessionId) return;
  try {
    if (JSON.parse(sessionStorage.getItem(storageKey(sessionId)) ?? "null")?.key === key)
      sessionStorage.removeItem(storageKey(sessionId));
  } catch { /* A successful submission does not depend on browser storage. */ }
}
