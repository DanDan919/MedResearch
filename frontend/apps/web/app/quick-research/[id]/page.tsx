import { notFound } from "next/navigation";
import { QuickResultWorkspace } from "../../../components/quick/quick-result-workspace";

export default async function QuickResultPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (!/^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(id)) notFound();
  return <QuickResultWorkspace runId={id} />;
}
