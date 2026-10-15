import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { PageHeader } from "@/components/page-header";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { KitConfirm } from "../../_components/kit-confirm";
import { KitEditor } from "../../_components/kit-editor";
import { approveKit, discardKit, restoreKit, saveKit } from "../../actions";

export const metadata: Metadata = { title: "Brand Kit version" };

export default async function VersionPage({ params }: { params: Promise<{ workspace: string; number: string }> }) {
  const { workspace: handle, number: value } = await params;
  const number = Number(value);
  if (!/^\d+$/.test(value) || !Number.isSafeInteger(number) || number < 1) notFound();
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const [versionResult, kitResult] = await Promise.all([
    api.GET("/api/brands/{brandId}/brand-kit/versions/{number}", { params: { path: { brandId: workspace.id, number } } }),
    api.GET("/api/brands/{brandId}/brand-kit", { params: { path: { brandId: workspace.id } } }),
  ]);
  const [version, kit] = await Promise.all([unwrap(versionResult), unwrap(kitResult)]);
  return <div className="flex flex-col gap-8">
    <PageHeader title={`Version ${version.number}`} eyebrow="Brand Kit" description="Review this saved version. Restoring copies it into a draft for approval." />
    <Link href={`/${handle}/brand-kit`} className="self-start text-sm font-medium text-primary hover:underline">Back to Brand Kit</Link>
    {version.status === "superseded" && workspace.roles.includes("admin") && <KitConfirm label="Restore into draft" title={`Restore version ${number}?`} description={kit.draft ? "This replaces the current draft. Review and approve the restored kit before agents use it." : "This creates a draft from the old version. Review and approve it before agents use it."} action={restoreKit.bind(null, workspace.id, number)} revision={kit.draft?.revision ?? 0} />}
    <KitEditor version={version} draftRevision={0} files={version.files} canEdit={false} readOnlyMessage="This saved version is read-only. Return to Brand Kit to edit the current draft." save={saveKit.bind(null, workspace.id)} approve={approveKit.bind(null, workspace.id)} discard={discardKit.bind(null, workspace.id)} />
    <ul className="flex flex-col gap-2">{version.files.map((file) => <li key={file.id}><a className="text-sm text-primary underline-offset-4 hover:underline" href={`/api/brands/${workspace.id}/brand-kit/files/${file.id}/download`}>{file.fileName}{file.deleted ? " (archived)" : ""}</a></li>)}</ul>
  </div>;
}
