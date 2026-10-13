"use client";

import { useActionState, useId } from "react";

import { ConfirmDialog } from "@/components/confirm-dialog";
import { FormStatus } from "@/components/form-status";
import { RoleCheckboxes } from "@/components/role-checkboxes";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { UserAvatar } from "@/components/user-avatar";
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
  const rolesForm = useId();

  return (
    <li className="flex flex-col gap-3 px-4 py-4 md:px-5">
      <div className="grid grid-cols-1 gap-3 xl:grid-cols-[18rem_auto_auto_minmax(0,1fr)] xl:items-center xl:gap-x-4">
        <div className="flex min-w-0 items-center gap-3">
          <UserAvatar name={member.name} pictureUrl={member.pictureUrl} isYou={isSelf} />
          <div className="min-w-0">
            <p className="flex min-w-0 gap-1 font-semibold text-foreground">
              <span className="truncate" title={member.name}>
                {member.name}
              </span>
              {isSelf && <span className="shrink-0 font-normal text-subtle-foreground">(you)</span>}
            </p>
            <p className="truncate text-small text-subtle-foreground" title={member.email}>
              {member.email}
            </p>
          </div>
        </div>
        {canManage ? (
          <form id={rolesForm} action={rolesAction} className="min-w-0">
            <RoleCheckboxes legend={`Roles for ${member.name}`} defaultValue={member.roles} />
          </form>
        ) : (
          <div className="flex flex-wrap gap-1.5">
            {member.roles.map((role) => (
              <Badge key={role}>{roleLabels[role]}</Badge>
            ))}
          </div>
        )}
        {(canManage || isSelf) && (
          <div className="flex items-center justify-between gap-2 xl:contents">
            {canManage && (
              <Button type="submit" form={rolesForm} variant="secondary" size="sm" pending={savingRoles}>
                Save roles
              </Button>
            )}
            <ConfirmDialog
              trigger={
                <Button variant="destructive" size="sm" pending={removing} className="xl:col-start-4 xl:justify-self-end">
                  {isSelf ? "Leave workspace" : "Remove"}
                </Button>
              }
              title={isSelf ? "Leave this workspace?" : `Remove ${member.name}?`}
              description={
                isSelf
                  ? "You lose access to this workspace straight away. An admin can invite you again."
                  : `${member.name} loses access to this workspace straight away. You can invite them again later.`
              }
              confirmLabel={isSelf ? "Leave workspace" : "Remove"}
              action={removeAction}
            />
          </div>
        )}
      </div>
      <FormStatus state={rolesState} />
      <FormStatus state={removeState} />
    </li>
  );
}
