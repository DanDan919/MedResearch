import { EmptyState } from "../../components/state-panel";

export default function StudiesPage() {
  return (
    <EmptyState
      title="Study browsing API not available"
      description="Study identity and provenance are persisted by the backend, but F1 intentionally does not add study list or search endpoints."
    />
  );
}
