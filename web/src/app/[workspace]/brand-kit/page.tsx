import type { Metadata } from "next";
import Link from "next/link";

import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { DraftingForm } from "./_components/drafting-form";
import { KitEditor } from "./_components/kit-editor";
import { KitFiles } from "./_components/kit-files";
import { KitHistoryList } from "./_components/kit-history";
import { approveKit, discardKit, draftKit, removeKitFile, saveKit } from "./actions";

export const metadata: Metadata = { title: "Brand Kit" };

export default async function BrandKitPage({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const options = { params: { path: { brandId: workspace.id } } };
  const [kitResult, filesResult, historyResult, runResult, detailsResult, membersResult] = await Promise.all([
    api.GET("/api/brands/{brandId}/brand-kit", options),
    api.GET("/api/brands/{brandId}/brand-kit/files", options),
    api.GET("/api/brands/{brandId}/brand-kit/versions", options),
    api.GET("/api/brands/{brandId}/brand-kit/drafting/latest", options),
    api.GET("/api/brands/{brandId}", options),
    api.GET("/api/brands/{brandId}/members", options),
  ]);
  const [kit, files, versions, run, details, members] = await Promise.all([
    unwrap(kitResult), unwrap(filesResult), unwrap(historyResult),
    runResult.response.status === 204 ? null : unwrap(runResult), unwrap(detailsResult), unwrap(membersResult),
  ]);
  const canEdit = workspace.roles.includes("admin");
  const names = Object.fromEntries(members.map((member) => [member.userId, member.name]));
  return <div className="flex flex-col gap-8">
    <PageHeader title="Brand Kit" description="Your brand's voice, visuals and rules, approved by you and used by every agent." actions={<Link className="inline-flex min-h-11 items-center text-small text-primary hover:underline" href={`/${handle}/brand-kit/memory`}>View Brand Memory</Link>} />
    <Alert variant={kit.approved ? "success" : "warning"}>{kit.approved ? `Agents use approved version ${kit.approved.number}. Draft changes need approval before agents use them.` : "No approved Brand Kit yet. Review and approve a version so agents can use it."}</Alert>
    <DraftingForm run={run ?? null} hasDraft={!!kit.draft} canEdit={canEdit} action={draftKit.bind(null, workspace.id)} />
    <KitEditor key={workspace.id} version={kit.draft ?? kit.approved} draftRevision={kit.draft?.revision ?? 0} files={files} canEdit={canEdit} save={saveKit.bind(null, workspace.id)} approve={approveKit.bind(null, workspace.id)} discard={discardKit.bind(null, workspace.id)} />
    <KitFiles brandId={workspace.id} files={files} canEdit={canEdit} remove={Object.fromEntries(files.map((file) => [file.id, removeKitFile.bind(null, workspace.id, file.id)]))} />
    <KitHistoryList versions={versions} handle={handle} timeZone={details.timeZone} names={names} />
  </div>;
}
