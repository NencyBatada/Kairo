const errors: Record<string, string> = {
  unverified_email: "Google hasn't verified the email on that account, so it can't be used to sign in.",
  sign_in_failed: "Sign-in didn't finish. Please try again.",
};

type SignInPanelProps = {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

export async function SignInPanel({ searchParams }: SignInPanelProps) {
  const { returnUrl, error } = await searchParams;
  const target = typeof returnUrl === "string" ? returnUrl : "/";
  const message = typeof error === "string" ? (errors[error] ?? errors.sign_in_failed) : null;

  return (
    <div className="space-y-4">
      {message && (
        <p role="alert" className="rounded-md bg-red-50 p-3 text-sm text-red-800 dark:bg-red-950 dark:text-red-200">
          {message}
        </p>
      )}
      <a
        href={`/api/auth/sign-in?returnUrl=${encodeURIComponent(target)}`}
        className="block rounded-md bg-black px-4 py-2 text-sm font-medium text-white hover:bg-black/80 dark:bg-white dark:text-black"
      >
        Sign in with Google
      </a>
    </div>
  );
}
