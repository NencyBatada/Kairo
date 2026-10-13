"use client";

import { useRouter } from "next/navigation";

import { REQUEST_HEADER } from "@/lib/http-names";

export function SignOutButton() {
  const router = useRouter();

  async function signOut() {
    await fetch("/api/auth/sign-out", { method: "POST", headers: { [REQUEST_HEADER]: "1" } });
    router.replace("/sign-in");
    router.refresh();
  }

  return (
    <button type="button" onClick={signOut} className="text-sm underline-offset-2 hover:underline">
      Sign out
    </button>
  );
}
