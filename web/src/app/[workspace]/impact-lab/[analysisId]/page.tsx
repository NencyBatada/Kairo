import Link from "next/link";
import { notFound } from "next/navigation";
import { PageHeader } from "@/components/page-header";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { LabWorkspace } from "../_components/lab-workspace";
import { validId } from "../_lib/model";

export default async function AnalysisPage({ params }: { params: Promise<{ workspace: string; analysisId: string }> }) {
  const { workspace: handle, analysisId } = await params;
  if (!validId(analysisId)) notFound();
  const workspace = await getWorkspace(handle);
  const analysis = await (await coreApi()).GET("/api/brands/{brandId}/impact-lab/analyses/{analysisId}", { params: { path: { brandId: workspace.id, analysisId } } }).then(unwrap);
  if (analysis.id.toLowerCase() !== analysisId.toLowerCase()) notFound();
  return <div className="min-w-0 space-y-8"><div className="[&_h1]:wrap-anywhere"><PageHeader title={analysis.name} eyebrow="Impact Lab" description={analysis.whatChanged} actions={<Link className="inline-flex min-h-11 items-center text-small text-primary hover:underline" href={`/${handle}/impact-lab`}>All analyses</Link>} /></div><LabWorkspace key={analysis.id} analysis={analysis} brandId={workspace.id} workspace={handle} canEdit={workspace.roles.some(role => role === "admin" || role === "marketer")} readToken={new Date().toISOString()} /></div>;
}
