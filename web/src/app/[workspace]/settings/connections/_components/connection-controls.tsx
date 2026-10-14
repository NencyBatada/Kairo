"use client";

import { useActionState } from "react";

import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormStatus } from "@/components/form-status";
import { Button } from "@/components/ui/button";
import { idle, type FormState } from "@/lib/forms";

type ConnectionControlsProps = {
  connected: boolean;
  productName: string;
  check: (state: FormState, formData: FormData) => Promise<FormState>;
  disconnect: (state: FormState, formData: FormData) => Promise<FormState>;
};

export function ConnectionControls({ connected, productName, check, disconnect }: ConnectionControlsProps) {
  const [checkState, checkAction, checking] = useActionState(check, idle);
  const [disconnectState, disconnectAction, disconnecting] = useActionState(disconnect, idle);
  const pending = checking || disconnecting;

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <div className="flex flex-wrap gap-2">
        {connected && (
          <form action={checkAction}>
            <Button type="submit" size="sm" variant="secondary" pending={pending}>Check access</Button>
          </form>
        )}
        <ConfirmDialog
          trigger={<Button variant="destructive" size="sm" pending={pending}>Disconnect</Button>}
          title={`Disconnect ${productName}?`}
          description={`This removes ${productName} from this workspace. You can connect it again later.`}
          confirmLabel="Disconnect"
          action={disconnectAction}
        />
      </div>
      <FormStatus state={checkState} />
      <FormStatus state={disconnectState} />
    </div>
  );
}
