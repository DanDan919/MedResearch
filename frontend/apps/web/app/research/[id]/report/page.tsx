import { ResearchReport } from "../../../../components/research/research-report";

export default async function ResearchReportPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <ResearchReport researchRunId={id} />;
}
