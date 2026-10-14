"use client";

import { useActionState } from "react";

import { FieldError } from "@/components/field-error";
import { FormStatus } from "@/components/form-status";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { ApprovalSettings } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type Duty = "briefApprovers" | "assetApprovers" | "launchApprovers" | "budgetOwners";

const duties: { field: Duty; label: string; help: string }[] = [
  { field: "briefApprovers", label: "Brief approvers", help: "Approve briefs, gate 1." },
  { field: "assetApprovers", label: "Asset approvers", help: "Approve the asset set, gate 2." },
  { field: "launchApprovers", label: "Launch approvers", help: "Approve launches, gate 3." },
  { field: "budgetOwners", label: "Budget owners", help: "Approve briefs and launches above the budget limit." },
];

type ApprovalsFormProps = {
  action: (state: FormState, formData: FormData) => Promise<FormState>;
  settings: ApprovalSettings;
  approvers: { userId: string; name: string }[];
  canEdit: boolean;
};

export function ApprovalsForm({ action, settings, approvers, canEdit }: ApprovalsFormProps) {
  const [state, formAction, pending] = useActionState(action, idle);
  const errors = state.fieldErrors ?? {};

  return (
    <form action={formAction} className="flex flex-col gap-6">
      {!canEdit && <Alert>Only admins can change who approves what.</Alert>}
      <fieldset disabled={!canEdit || pending} className="flex min-w-0 flex-col gap-6">
        <div className="grid gap-4 md:grid-cols-2">
          {duties.map((duty) => (
            <Card key={duty.field} className="min-w-0">
              <fieldset className="min-w-0 px-4 md:px-5">
                <legend className="text-base font-semibold text-foreground">{duty.label}</legend>
                <p className="mt-1 text-small text-subtle-foreground">{duty.help}</p>
                {approvers.length === 0 ? (
                  <p className="mt-4 text-small text-muted-foreground">Nobody has the approver role yet. Give someone the role on the Team page.</p>
                ) : (
                  <ul className="mt-4 flex flex-col gap-2.5">
                    {approvers.map((approver) => (
                      <li key={approver.userId}>
                        <label className="flex min-h-6 cursor-pointer items-center gap-2.5 text-sm text-foreground pointer-coarse:min-h-11">
                          <Checkbox name={duty.field} value={approver.userId} defaultChecked={settings[duty.field].includes(approver.userId)} />
                          <span className="min-w-0 truncate" title={approver.name}>
                            {approver.name}
                          </span>
                        </label>
                      </li>
                    ))}
                  </ul>
                )}
                <div className="mt-2">
                  <FieldError id={`${duty.field}-error`} errors={errors[duty.field]} />
                </div>
              </fieldset>
            </Card>
          ))}
        </div>
        <Card>
          <CardContent className="flex flex-col gap-1.5">
            <Label htmlFor="budgetLimit" className="text-base">
              Per-campaign budget limit ({settings.currency})
            </Label>
            <p id="budgetLimit-help" className="text-small text-subtle-foreground">
              Leave it empty and every brief and launch goes to a budget owner.
            </p>
            <div className="relative mt-1.5 max-w-xs">
              <span aria-hidden="true" className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-small font-medium text-subtle-foreground">
                {settings.currency}
              </span>
              <Input
                id="budgetLimit"
                name="budgetLimit"
                type="number"
                min="0"
                step="0.01"
                inputMode="decimal"
                defaultValue={settings.budgetLimit ?? ""}
                aria-describedby="budgetLimit-help budgetLimit-error"
                aria-invalid={errors.budgetLimit ? true : undefined}
                className="pl-14 tabular-nums"
              />
            </div>
            <FieldError id="budgetLimit-error" errors={errors.budgetLimit} />
          </CardContent>
        </Card>
      </fieldset>
      {canEdit && (
        <div className="flex flex-col gap-3 max-lg:sticky max-lg:bottom-0 max-lg:z-30 max-lg:-mx-4 max-lg:border-t max-lg:border-border max-lg:bg-card/95 max-lg:px-4 max-lg:py-4 max-lg:backdrop-blur max-lg:pb-[max(1rem,env(safe-area-inset-bottom))] md:-mx-6 md:px-6 lg:static lg:mx-0 lg:border-0 lg:bg-transparent lg:px-0 lg:py-0 lg:backdrop-blur-none">
          <Button type="submit" pending={pending} className="self-start max-lg:w-full lg:self-start">
            Save approval settings
          </Button>
          <FormStatus state={state} />
        </div>
      )}
    </form>
  );
}
