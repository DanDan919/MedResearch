"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useSyncExternalStore } from "react";

type Theme = "light" | "dark" | "system";
type ResolvedTheme = "light" | "dark";

type ThemeContextValue = {
  theme: Theme;
  resolvedTheme: ResolvedTheme;
  setTheme: (theme: Theme) => void;
};

const storageKey = "medresearch-theme";
const ThemeContext = createContext<ThemeContextValue | null>(null);
let browserTheme: Theme = "system";
let browserResolvedTheme: ResolvedTheme = "light";
const listeners = new Set<() => void>();

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function notify() {
  listeners.forEach((listener) => listener());
}

function getSystemTheme(): ResolvedTheme {
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

function resolveTheme(theme: Theme, enableSystem: boolean): ResolvedTheme {
  return theme === "system" && enableSystem ? getSystemTheme() : theme === "dark" ? "dark" : "light";
}

function applyTheme(theme: ResolvedTheme) {
  const root = document.documentElement;
  root.classList.remove("light", "dark");
  root.classList.add(theme);
  root.style.colorScheme = theme;
}

export function AppThemeProvider({
  children,
  defaultTheme = "system",
  enableSystem = true
}: {
  children: React.ReactNode;
  defaultTheme?: Theme;
  enableSystem?: boolean;
}) {
  const theme = useSyncExternalStore(subscribe, () => browserTheme, () => defaultTheme);
  const resolvedTheme = useSyncExternalStore<ResolvedTheme>(subscribe, () => browserResolvedTheme, () => "light");

  useEffect(() => {
    const stored = window.localStorage.getItem(storageKey);
    const initialTheme: Theme = stored === "light" || stored === "dark" || stored === "system" ? stored : defaultTheme;
    browserTheme = initialTheme;
    browserResolvedTheme = resolveTheme(initialTheme, enableSystem);
    applyTheme(browserResolvedTheme);
    notify();
  }, [defaultTheme, enableSystem]);

  useEffect(() => {
    const updateTheme = () => {
      const nextResolvedTheme = resolveTheme(browserTheme, enableSystem);
      browserResolvedTheme = nextResolvedTheme;
      applyTheme(nextResolvedTheme);
      notify();
    };

    updateTheme();
    if (!enableSystem || browserTheme !== "system") {
      return;
    }

    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    mediaQuery.addEventListener("change", updateTheme);
    return () => mediaQuery.removeEventListener("change", updateTheme);
  }, [enableSystem, theme]);

  const setTheme = useCallback((nextTheme: Theme) => {
    browserTheme = nextTheme;
    browserResolvedTheme = resolveTheme(nextTheme, enableSystem);
    window.localStorage.setItem(storageKey, nextTheme);
    applyTheme(browserResolvedTheme);
    notify();
  }, [enableSystem]);

  const value = useMemo(() => ({ theme, resolvedTheme, setTheme }), [resolvedTheme, setTheme, theme]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useAppTheme() {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useAppTheme must be used inside AppThemeProvider.");
  }

  return context;
}
