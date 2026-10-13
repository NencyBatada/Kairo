import { CircleAlert } from "lucide-react";

export function FieldError({ id, errors }: { id: string; errors?: string[] }) {
  if (!errors?.length) {
    return null;
  }

  return (
    <p id={id} className="flex items-start gap-1.5 text-small text-destructive-strong">
      <CircleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
      <span>{errors.join(" ")}</span>
    </p>
  );
}
