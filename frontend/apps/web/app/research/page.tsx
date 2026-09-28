import { Suspense } from "react";
import { ResearchHistory } from "../../components/research/research-history";

export default function ResearchRunsPage() {
  return (
    <Suspense fallback={<div className="text-sm text-muted-foreground">Loading research history...</div>}>
      <ResearchHistory />
    </Suspense>
  );
}
