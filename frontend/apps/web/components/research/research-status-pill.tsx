import { Badge } from "@medresearch/ui";
import { researchRunStatusPresentation } from "@medresearch/api";
import type { ResearchRunStatus } from "@medresearch/api";

export function ResearchStatusPill({ status }: { status: ResearchRunStatus }) {
  const presentation = researchRunStatusPresentation[status];

  return <Badge tone={presentation.tone}>{presentation.label}</Badge>;
}
