import type { Metadata } from "next";
import { PageHeader } from "@/components/page-header";
import { Alert } from "@/components/ui/alert";
import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import { RuleForm } from "../_components/rule-form";
import { previewOrSaveRule } from "../actions";

export const metadata: Metadata = { title: "Create trigger rule" };

export default async function NewRulePage({ params }: { params: Promise<{ workspace: string }> }) {
  const { workspace: handle } = await params;
  const workspace = await getWorkspace(handle);
  if (!workspace.roles.includes("admin")) return <div className="flex flex-col gap-8"><PageHeader title="Create trigger rule" /><Alert>Only workspace admins can create trigger rules.</Alert></div>;
  const api = await coreApi();
  const path = { brandId: workspace.id };
  const [options, festivals] = await Promise.all([api.GET("/api/brands/{brandId}/triggers/options", { params: { path } }).then(unwrap), api.GET("/api/brands/{brandId}/triggers/festivals", { params: { path } }).then(unwrap)]);
  return <div className="flex flex-col gap-8"><PageHeader eyebrow="Triggers" title="Create trigger rule" description="Set the condition, then review the rule in plain words before saving." /><RuleForm options={options} festivals={festivals} handle={handle} action={previewOrSaveRule.bind(null, workspace.id, null)} /></div>;
}
