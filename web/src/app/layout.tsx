import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

import { NavMenu } from "@/components/nav-menu";

import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Kairo",
  description:
    "From a rising demand signal to a live, on-brand Google Ads campaign, with proof of the lift it caused.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="flex min-h-full">
        <aside className="w-56 shrink-0 border-r border-black/10 p-4 dark:border-white/15">
          <p className="mb-6 px-3 text-lg font-semibold">Kairo</p>
          <NavMenu />
        </aside>
        <main className="flex-1 p-8">{children}</main>
      </body>
    </html>
  );
}
