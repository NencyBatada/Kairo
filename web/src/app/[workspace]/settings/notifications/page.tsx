import type { Metadata } from "next";

import { coreApi, unwrap } from "@/lib/core-api/client";
import { getWorkspace } from "@/lib/core-api/queries";

import { EmailSettingsForm } from "./_components/email-settings-form";
import { NotificationInbox } from "./_components/notification-inbox";
import { saveEmailSettings } from "./actions";

export const metadata: Metadata = { title: "Notifications" };

export default async function NotificationsPage({
  params,
  searchParams,
}: PageProps<"/[workspace]/settings/notifications">) {
  const [{ workspace: handle }, { before }] = await Promise.all([params, searchParams]);
  const workspace = await getWorkspace(handle);
  const api = await coreApi();
  const brandId = workspace.id;

  const [page, emailSettings, details] = await Promise.all([
    unwrap(
      await api.GET("/api/brands/{brandId}/notifications", {
        params: {
          path: { brandId },
          query: { before: typeof before === "string" ? before : undefined, limit: 30 },
        },
      }),
    ),
    unwrap(await api.GET("/api/brands/{brandId}/notifications/email-settings", { params: { path: { brandId } } })),
    unwrap(await api.GET("/api/brands/{brandId}", { params: { path: { brandId } } })),
  ]);

  return (
    <div className="flex flex-col gap-10">
      <section aria-labelledby="inbox-title" className="flex flex-col gap-4">
        <div>
          <h2 id="inbox-title" className="font-display text-display-sm text-foreground">
            In-app notifications
          </h2>
          <p className="mt-1 max-w-prose text-small text-muted-foreground">Links take you to where you can act.</p>
        </div>
        <NotificationInbox
          brandId={brandId}
          workspace={handle}
          page={page}
          timeZone={details.timeZone}
        />
      </section>
      <section aria-labelledby="email-settings-title" className="flex flex-col gap-4">
        <div>
          <h2 id="email-settings-title" className="font-display text-display-sm text-foreground">
            Email settings
          </h2>
          <p className="mt-1 max-w-prose text-small text-muted-foreground">These apply to you in this workspace only.</p>
        </div>
        <EmailSettingsForm brandId={brandId} settings={emailSettings} action={saveEmailSettings} />
      </section>
    </div>
  );
}
