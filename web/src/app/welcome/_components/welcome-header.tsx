import { SignOutButton } from "@/components/sign-out-button";
import { SiteHeader } from "@/components/site-header";

export function WelcomeHeader() {
  return (
    <SiteHeader>
      <SignOutButton />
    </SiteHeader>
  );
}
