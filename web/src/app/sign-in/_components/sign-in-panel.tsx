import { Alert } from "@/components/ui/alert";
import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

import { GoogleMark } from "./google-mark";

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
    <div className="flex flex-col gap-4">
      {message && <Alert variant="destructive">{message}</Alert>}
      <a
        href={`/api/auth/sign-in?returnUrl=${encodeURIComponent(target)}`}
        className={cn(buttonVariants({ variant: "secondary", size: "lg" }), "w-full gap-3")}
      >
        <GoogleMark className="size-[1.125rem]" />
        Continue with Google
      </a>
    </div>
  );
}
