import Link from "next/link";
import { Button } from "@medresearch/ui";
import { EmptyState } from "../../components/state-panel";

export default function ResearchRunsPage() {
  return (
    <div className="space-y-4">
      <EmptyState
        title="Research run list API not available"
        description="The backend currently exposes create, get-by-id, and report-by-run endpoints, but not a paginated research-run list."
      />
      <Button asChild>
        <Link href="/research/new">Create a run</Link>
      </Button>
    </div>
  );
}
