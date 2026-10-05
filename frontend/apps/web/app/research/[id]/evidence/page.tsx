import { EvidenceProvenanceWorkspace } from "../../../../components/research/evidence-provenance-workspace";

export default async function EvidenceProvenancePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <EvidenceProvenanceWorkspace researchRunId={id} />;
}
