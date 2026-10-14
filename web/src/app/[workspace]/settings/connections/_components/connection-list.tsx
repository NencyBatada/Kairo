import { Alert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import type { ConnectionView, ProductAccount } from "@/lib/core-api/types";
import type { FormState } from "@/lib/forms";
import { cn } from "@/lib/utils";

import { AccountPicker } from "./account-picker";
import { ConnectionControls } from "./connection-controls";

type ConnectionListProps = {
  connections: ConnectionView[];
  canManage: boolean;
  brandId: string;
  callbackError?: string;
  callbackChoose?: string;
  accountChoices?: Record<string, { accounts?: ProductAccount[]; error?: string }>;
  chooseAccount?: (product: string, state: FormState, formData: FormData) => Promise<FormState>;
  checkConnection?: (product: string, state: FormState, formData: FormData) => Promise<FormState>;
  disconnectConnection?: (product: string, state: FormState, formData: FormData) => Promise<FormState>;
};

const states = {
  notConnected: { label: "Not connected", variant: "info" },
  choosingAccount: { label: "Choose account", variant: "warning" },
  connected: { label: "Connected", variant: "success" },
  needsReconnect: { label: "Needs reconnection", variant: "warning" },
} as const;

function callbackMessage(code: string) {
  switch (code) {
    case "cancelled":
      return "Google consent was cancelled.";
    case "expired":
      return "Google consent expired. Please try again.";
    case "scopes_missing":
      return "The requested Google access was not granted.";
    default:
      return "Google connection failed. Please try again.";
  }
}

export function ConnectionList({ connections, canManage, brandId, callbackError, callbackChoose, accountChoices, chooseAccount, checkConnection, disconnectConnection }: ConnectionListProps) {
  return (
    <section aria-labelledby="connections-heading" className="flex flex-col gap-4">
      <div>
        <h2 id="connections-heading" className="text-base font-semibold text-foreground">Connections</h2>
        <p className="text-small text-subtle-foreground">Google accounts available to this workspace.</p>
      </div>
      {callbackError && <Alert variant="destructive">{callbackMessage(callbackError)}</Alert>}
      {!canManage && <Alert>Only workspace admins can manage connections.</Alert>}
      <Card className="gap-0 py-0 md:gap-0 md:py-0">
        {connections.length === 0 ? (
          <p className="px-4 py-6 text-small text-muted-foreground md:px-5">No connection products are available right now.</p>
        ) : (
          <ul className="divide-y divide-border">
            {connections.map((connection) => {
              const state = states[connection.state];
              const connectHref = `/api/brands/${encodeURIComponent(brandId)}/connections/${encodeURIComponent(connection.product)}/connect`;
              return (
                <li
                  key={connection.product}
                  aria-label={connection.name}
                  className={cn("min-w-0 px-4 py-4 md:px-5", callbackChoose === connection.product && connection.state === "choosingAccount" && "border-l-4 border-primary")}
                >
                  <div className="flex min-w-0 flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
                    <div className="flex min-w-0 flex-col gap-2">
                      <div className="flex min-w-0 flex-wrap items-center gap-2.5">
                        <h3 className="min-w-0 font-semibold wrap-anywhere text-foreground">{connection.name}</h3>
                        <Badge variant={state.variant}>{state.label}</Badge>
                      </div>
                      {connection.displayName && <p className="text-small wrap-anywhere text-foreground">{connection.displayName}</p>}
                      {connection.connectedByName && <p className="text-small wrap-anywhere text-subtle-foreground">Connected by <span>{connection.connectedByName}</span></p>}
                      {connection.checkedAt && (
                        <p className="text-small text-subtle-foreground">
                          Last checked <time dateTime={connection.checkedAt}>{new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short", timeZone: "UTC" }).format(new Date(connection.checkedAt))} UTC</time>
                        </p>
                      )}
                      {connection.reconnectReason && <p className="text-small wrap-anywhere text-warning-strong">{connection.reconnectReason}</p>}
                    </div>
                    {canManage && (
                      <div className="flex min-w-0 flex-col items-start gap-2">
                        {connection.state !== "connected" && (
                          <a className={buttonVariants({ variant: "secondary", size: "sm" })} href={connectHref}>
                            {connection.state === "needsReconnect" ? "Reconnect" : connection.state === "choosingAccount" ? "Connect again" : "Connect"}
                          </a>
                        )}
                        {connection.state !== "notConnected" && checkConnection && disconnectConnection && (
                          <ConnectionControls
                            connected={connection.state === "connected"}
                            productName={connection.name}
                            check={checkConnection.bind(null, connection.product)}
                            disconnect={disconnectConnection.bind(null, connection.product)}
                          />
                        )}
                      </div>
                    )}
                  </div>
                  {canManage && connection.state === "choosingAccount" && chooseAccount && (
                    <div className="mt-4 min-w-0">
                      <AccountPicker
                        accounts={accountChoices?.[connection.product]?.accounts ?? []}
                        error={accountChoices?.[connection.product]?.error}
                        action={chooseAccount.bind(null, connection.product)}
                      />
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </Card>
    </section>
  );
}
