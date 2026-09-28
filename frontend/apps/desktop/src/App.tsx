import { useEffect, useState } from "react";
import { Activity, ExternalLink } from "lucide-react";
import { MedResearchApiClient, normalizeApiBaseUrl } from "@medresearch/api";

const apiBaseUrl = normalizeApiBaseUrl(import.meta.env.VITE_MEDRESEARCH_API_URL);

export function App() {
  const [health, setHealth] = useState<"checking" | "connected" | "unavailable">("checking");

  useEffect(() => {
    const controller = new AbortController();
    const client = new MedResearchApiClient({ baseUrl: apiBaseUrl });

    client
      .getReadyHealth(controller.signal)
      .then(setHealth)
      .catch(() => setHealth("unavailable"));

    return () => controller.abort();
  }, []);

  return (
    <main className="shell">
      <section className="panel">
        <div className="brand">
          <Activity />
          <div>
            <h1>MedResearch Desktop</h1>
            <p>Windows shell for the shared React client foundation.</p>
          </div>
        </div>

        <dl>
          <div>
            <dt>API</dt>
            <dd>{apiBaseUrl}</dd>
          </div>
          <div>
            <dt>Readiness</dt>
            <dd>{health}</dd>
          </div>
        </dl>

        <p className="note">
          Scientific processing, evidence evaluation, and quantitative synthesis remain in the backend. The desktop shell
          does not implement local scientific logic.
        </p>

        <a href={apiBaseUrl} target="_blank" rel="noreferrer">
          Open API host <ExternalLink size={16} />
        </a>
      </section>
    </main>
  );
}
