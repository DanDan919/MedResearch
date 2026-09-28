import { Badge } from "@medresearch/ui";
import type { ResearchRunStatus } from "@medresearch/api";

export function ResearchStatusPill({ status }: { status: ResearchRunStatus }) {
  const tone =
    status === "Completed"
      ? "success"
      : status === "Failed"
        ? "danger"
        : status === "Cancelled"
          ? "warning"
          : "neutral";

  return <Badge tone={tone}>{status}</Badge>;
}
