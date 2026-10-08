"use client";

import { createContext, useContext } from "react";
import type { PublicSession } from "../lib/auth/types";

export const WebSessionContext = createContext<{ session: PublicSession; endSession: () => void }>({
  session: { authenticated: false, mode: "unavailable", sessionId: null, displayName: null, expiresAt: null }, endSession: () => undefined
});
export const useWebSession = () => useContext(WebSessionContext);
