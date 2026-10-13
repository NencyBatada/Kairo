import { roleLabels, type Role } from "@/lib/core-api/types";

const roles: Role[] = ["admin", "marketer", "approver"];

type RoleCheckboxesProps = {
  legend: string;
  defaultValue?: Role[];
};

export function RoleCheckboxes({ legend, defaultValue = [] }: RoleCheckboxesProps) {
  return (
    <fieldset className="flex flex-wrap gap-4">
      <legend className="sr-only">{legend}</legend>
      {roles.map((role) => (
        <label key={role} className="flex items-center gap-1 text-sm">
          <input type="checkbox" name="roles" value={role} defaultChecked={defaultValue.includes(role)} />
          {roleLabels[role]}
        </label>
      ))}
    </fieldset>
  );
}
