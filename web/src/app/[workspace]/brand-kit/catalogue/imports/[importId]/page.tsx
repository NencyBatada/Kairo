import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { PageHeader } from "@/components/page-header";
import { ImportResult } from "../../_components/import-result";
import { catalogueJson, catalogueRequest } from "../../_lib/api";
import type { CatalogueImport } from "../../_lib/types";
import { getWorkspace } from "@/lib/core-api/queries";

export const metadata: Metadata = { title: "Catalogue import" };
export default async function ImportPage({ params }: { params: Promise<{ workspace: string; importId: string }> }) {
  const { workspace: handle, importId } = await params;
  const workspace = await getWorkspace(handle);
  const response = await catalogueRequest(workspace.id, `/imports/${encodeURIComponent(importId)}`);
  if (response.status === 404) notFound();
  const result = await catalogueJson<CatalogueImport>(response);
  return <div className="flex flex-col gap-8"><PageHeader title="Import details" eyebrow="Catalogue" description="What changed and which rows need attention." /><Link href={`/${handle}/brand-kit/catalogue`} className="self-start text-small text-primary hover:underline">Back to catalogue</Link><ImportResult result={result} handle={handle} detail /></div>;
}
