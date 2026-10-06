"use client";

import { Moon, Sun } from "lucide-react";
import { useSyncExternalStore } from "react";
import { Button } from "@medresearch/ui";
import { useAppTheme } from "./app-theme-provider";

export function ThemeToggle() {
  const { resolvedTheme, setTheme } = useAppTheme();
  const mounted = useSyncExternalStore(
    () => () => undefined,
    () => true,
    () => false
  );

  const nextTheme = resolvedTheme === "dark" ? "light" : "dark";
  const label = mounted ? `Switch to ${nextTheme} theme` : "Toggle theme";

  return (
    <Button
      type="button"
      variant="ghost"
      size="sm"
      onClick={() => setTheme(nextTheme)}
      aria-label={label}
      title={label}
      disabled={!mounted}
    >
      {mounted && resolvedTheme === "dark" ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
    </Button>
  );
}
