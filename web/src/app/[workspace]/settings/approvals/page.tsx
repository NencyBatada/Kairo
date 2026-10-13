import type { Metadata } from "next";

import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { saveApprovalSettings } from "../actions";
import { ApprovalsForm } from "./_components/approvals-form";

export const metadata: Metadata = { title: "Approvals" };

export default async function ApprovalsPage({ params }: PageProps<"/[workspace]/settings/approvals">) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const brandId = workspace.id;
  const [settings, members] = await Promise.all([
    unwrap(await api.GET("/api/brands/{brandId}/approval-settings", { params: { path: { brandId } } })),
    unwrap(await api.GET("/api/brands/{brandId}/members", { params: { path: { brandId } } })),
  ]);

  return (
    <ApprovalsForm
      action={saveApprovalSettings.bind(null, brandId)}
      settings={settings}
      approvers={members.filter((member) => member.roles.includes("approver"))}
      canEdit={workspace.roles.includes("admin")}
    />
  );
}
