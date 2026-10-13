export function FieldError({ id, errors }: { id: string; errors?: string[] }) {
  if (!errors?.length) {
    return null;
  }

  return (
    <p id={id} className="text-sm text-red-700 dark:text-red-300">
      {errors.join(" ")}
    </p>
  );
}
