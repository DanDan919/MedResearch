import { ResearchDetail } from "../../../components/research/research-detail";

export default async function ResearchDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <ResearchDetail researchRunId={id} />;
}
