"use client";

import { useActionState } from "react";

import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";
import { roleLabels, type Member } from "@/lib/core-api/types";
import { idle, type FormState } from "@/lib/forms";

type MemberRowProps = {
  member: Member;
  isSelf: boolean;
  canManage: boolean;
  changeRoles: (state: FormState, formData: FormData) => Promise<FormState>;
  remove: (state: FormState, formData: FormData) => Promise<FormState>;
};

export function MemberRow({ member, isSelf, canManage, changeRoles, remove }: MemberRowProps) {
  const [rolesState, rolesAction, savingRoles] = useActionState(changeRoles, idle);
  const [removeState, removeAction, removing] = useActionState(remove, idle);
  const removeLabel = isSelf ? "Leave workspace" : "Remove";

  return (
    <li className="space-y-2 py-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <p className="font-medium">
            {member.name}
            {isSelf && " (you)"}
          </p>
          <p className="text-sm text-zinc-600 dark:text-zinc-400">{member.email}</p>
        </div>
        {(canManage || isSelf) && (
          <form
            action={removeAction}
            onSubmit={(event) => {
              if (!window.confirm(isSelf ? "Leave this workspace?" : `Remove ${member.name} from this workspace?`)) {
                event.preventDefault();
              }
            }}
          >
            <button type="submit" disabled={removing} className="text-sm text-red-700 hover:underline dark:text-red-300">
              {removeLabel}
            </button>
          </form>
        )}
      </div>
      {canManage ? (
        <form action={rolesAction} className="flex flex-wrap items-center gap-4">
          <RoleCheckboxes legend={`Roles for ${member.name}`} defaultValue={member.roles} />
          <button type="submit" disabled={savingRoles} className="rounded-md border px-3 py-1 text-sm">
            Save roles
          </button>
        </form>
      ) : (
        <p className="text-sm">{member.roles.map((role) => roleLabels[role]).join(", ")}</p>
      )}
      <FormStatus state={rolesState} />
      <FormStatus state={removeState} />
    </li>
  );
}
