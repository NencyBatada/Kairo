import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { getWorkspace } from "@/lib/core-api/queries";
import { NewAnalysis } from "../_components/new-analysis";

export default async function NewAnalysisPage({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace: handle } = await params, workspace = await getWorkspace(handle);
  return <div className="min-w-0 space-y-8"><PageHeader title="Measure a change" eyebrow="Impact Lab" description="Start with what changed and the outcome you want to measure." actions={<Link className="inline-flex min-h-11 items-center text-small text-primary hover:underline" href={`/${handle}/impact-lab`}>All analyses</Link>} /><NewAnalysis brandId={workspace.id} workspace={handle} canEdit={workspace.roles.some(role => role === "admin" || role === "marketer")} /></div>;
}
