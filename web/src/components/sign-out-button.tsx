"use client";

import { LogOut } from "lucide-react";
import { useRouter } from "next/navigation";

import { Button } from "@/components/ui/button";
import { REQUEST_HEADER } from "@/lib/http-names";

export function useSignOut() {
  const router = useRouter();

  return async function signOut() {
    await fetch("/api/auth/sign-out", { method: "POST", headers: { [REQUEST_HEADER]: "1" } });
    router.replace("/sign-in");
    router.refresh();
  };
}

export function SignOutButton() {
  const signOut = useSignOut();

  return (
    <Button variant="ghost" size="sm" onClick={signOut}>
      <LogOut aria-hidden="true" />
      Sign out
    </Button>
  );
}
