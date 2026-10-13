"use client";

import { useActionState } from "react";

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
  const [removeState, removeAction] = useActionState(remove, idle);

  return (
    <li className="flex flex-col gap-3 px-4 py-4 md:px-5">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-center">
        <div className="flex min-w-0 items-center gap-3 lg:w-72 lg:shrink-0">
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
          <form action={rolesAction} className="flex min-w-0 flex-1 flex-wrap items-center gap-3">
            <RoleCheckboxes legend={`Roles for ${member.name}`} defaultValue={member.roles} />
            <Button type="submit" variant="secondary" size="sm" pending={savingRoles}>
              Save roles
            </Button>
          </form>
        ) : (
          <div className="flex flex-1 flex-wrap gap-1.5">
            {member.roles.map((role) => (
              <Badge key={role}>{roleLabels[role]}</Badge>
            ))}
          </div>
        )}
        {(canManage || isSelf) && (
          <ConfirmDialog
            trigger={
              <Button variant="destructive" size="sm" className="self-start lg:self-auto">
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
        )}
      </div>
      <FormStatus state={rolesState} />
      <FormStatus state={removeState} />
    </li>
  );
}
