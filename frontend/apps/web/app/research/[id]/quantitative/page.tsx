import { QuantitativeWorkspace } from "../../../../components/research/quantitative/quantitative-workspace";

export default async function QuantitativePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <QuantitativeWorkspace researchRunId={id} />;
}
