export type PublicSession = {
  authenticated: boolean;
  mode: "oidc" | "development-local" | "unavailable";
  sessionId: string | null;
  displayName: string | null;
  expiresAt: number | null;
};
