import type { Metadata } from "next";
import "./globals.css";
import { AppProviders } from "../components/app-providers";
import { AppShell } from "../components/app-shell";
import { cookies } from "next/headers";
import { readAuthConfig } from "../lib/auth/config";
import { publicSession, readPrivateSession, sessionOptions } from "../lib/auth/session";

export const metadata: Metadata = {
  title: "MedResearch",
  description: "Evidence synthesis research workspace"
};

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const config = readAuthConfig();
  const cookieStore = await cookies();
  const privateSession = config.mode === "oidc" ? await readPrivateSession(cookieStore.get(sessionOptions(config).cookieName)?.value, config) : null;
  return (
    <html lang="en" suppressHydrationWarning>
      <body>
        <AppProviders initialSession={publicSession(config, privateSession)}>
          <AppShell>{children}</AppShell>
        </AppProviders>
      </body>
    </html>
  );
}
