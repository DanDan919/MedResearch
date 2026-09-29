import { ResearchReportWorkspace } from "./research-report-workspace";

export function ResearchReport({ researchRunId }: { researchRunId: string }) {
  return <ResearchReportWorkspace researchRunId={researchRunId} />;
}
