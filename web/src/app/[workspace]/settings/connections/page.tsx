import type { Metadata } from "next";

import { coreApi, signInAgain, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";
import type { ConnectionView, ProductAccount } from "@/lib/core-api/types";
import { problemToFormState } from "@/lib/forms";

import { ConnectionList } from "./_components/connection-list";
import { checkConnection, chooseConnectionAccount, connectWithCredentials, disconnectConnection } from "./actions";

export const metadata: Metadata = { title: "Connections" };

export default async function ConnectionsPage({ params, searchParams }: PageProps<"/[workspace]/settings/connections">) {
  const { workspace: handle } = await params;
  const { error, choose } = await searchParams;
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const [connectionsResult, detailsResult] = await Promise.all([
    api.GET("/api/brands/{brandId}/connections", { params: { path: { brandId: workspace.id } } }),
    api.GET("/api/brands/{brandId}", { params: { path: { brandId: workspace.id } } }),
  ]);
  const connections = await unwrap(connectionsResult);
  const details = await unwrap(detailsResult);
  const canManage = workspace.roles.includes("admin");
  const accountChoices: Record<string, { accounts?: ProductAccount[]; error?: string }> = {};

  if (canManage) {
    const choosing = connections.filter((connection) => connection.state === "choosingAccount");
    const results = await Promise.all(choosing.map(async (connection: ConnectionView) => {
      const result = await api.GET("/api/brands/{brandId}/connections/{product}/accounts", {
        params: { path: { brandId: workspace.id, product: connection.product } },
      }).catch(() => null);
      if (!result) {
        return [connection.product, { error: "Could not load accounts. Reload this page to try again." }] as const;
      }
      if (result.response.status === 401) {
        return signInAgain();
      }
      if (!result.response.ok) {
        return [connection.product, { error: problemToFormState(result.error, result.response.status).message ?? "Could not load accounts. Reload this page to try again." }] as const;
      }
      return [connection.product, { accounts: result.data ?? [] }] as const;
    }));
    Object.assign(accountChoices, Object.fromEntries(results));
  }

  return (
    <ConnectionList
      connections={connections}
      canManage={canManage}
      brandId={workspace.id}
      timeZone={details.timeZone}
      callbackError={typeof error === "string" ? error : undefined}
      callbackChoose={typeof choose === "string" ? choose : undefined}
      accountChoices={accountChoices}
      chooseAccount={chooseConnectionAccount.bind(null, workspace.id)}
      checkConnection={checkConnection.bind(null, workspace.id)}
      disconnectConnection={disconnectConnection.bind(null, workspace.id)}
      connectWithCredentials={connectWithCredentials.bind(null, workspace.id)}
    />
  );
}
