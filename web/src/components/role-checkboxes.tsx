import { ChoiceChip } from "@/components/ui/choice-chip";
import { roleLabels, type Role } from "@/lib/core-api/types";

const roles: Role[] = ["admin", "marketer", "approver"];

type RoleCheckboxesProps = {
  legend: string;
  defaultValue?: Role[];
};

export function RoleCheckboxes({ legend, defaultValue = [] }: RoleCheckboxesProps) {
  return (
    <fieldset className="flex min-w-0 flex-wrap gap-2">
      <legend className="sr-only">{legend}</legend>
      {roles.map((role) => (
        <ChoiceChip key={role} name="roles" value={role} defaultChecked={defaultValue.includes(role)}>
          {roleLabels[role]}
        </ChoiceChip>
      ))}
    </fieldset>
  );
}
