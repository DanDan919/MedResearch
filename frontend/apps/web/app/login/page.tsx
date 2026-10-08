import { LoginForm } from "../../components/login-form";
import { readAuthConfig, safeReturnPath } from "../../lib/auth/config";

export const dynamic = "force-dynamic";
export default async function LoginPage({ searchParams }: { searchParams: Promise<{ returnTo?: string; reason?: string }> }) {
  const query = await searchParams;
  return <LoginForm configured={readAuthConfig().mode === "oidc"} returnTo={safeReturnPath(query.returnTo)} reason={query.reason ?? null} />;
}
