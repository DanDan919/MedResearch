"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Activity, ClipboardList, FlaskConical, Home, Microscope, Settings } from "lucide-react";
import { cn, Separator } from "@medresearch/ui";
import { ApiStatusIndicator } from "./api-status-indicator";
import { ThemeToggle } from "./theme-toggle";

const navItems = [
  { href: "/", label: "Dashboard", icon: Home },
  { href: "/research/new", label: "New Research", icon: Microscope },
  { href: "/research", label: "Research Runs", icon: ClipboardList },
  { href: "/studies", label: "Studies", icon: FlaskConical },
  { href: "/settings", label: "Settings", icon: Settings }
];

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();

  return (
    <div className="min-h-screen">
      <header className="sticky top-0 z-20 border-b border-border bg-surface/95 backdrop-blur">
        <div className="flex h-14 items-center justify-between px-4">
          <Link href="/" className="flex items-center gap-2 font-semibold">
            <Activity className="h-5 w-5 text-primary" />
            MedResearch
          </Link>
          <div className="flex items-center gap-3">
            <ApiStatusIndicator />
            <ThemeToggle />
          </div>
        </div>
      </header>

      <div className="grid min-h-[calc(100vh-3.5rem)] md:grid-cols-[224px_1fr]">
        <aside className="border-b border-border bg-surface md:border-b-0 md:border-r">
          <nav className="flex gap-2 overflow-x-auto p-3 md:flex-col md:gap-1">
            {navItems.map((item) => {
              const active = item.href === "/" ? pathname === "/" : pathname.startsWith(item.href);
              const Icon = item.icon;
              return (
                <Link
                  key={item.href}
                  href={item.href}
                  className={cn(
                    "inline-flex h-9 shrink-0 items-center gap-2 rounded-md px-3 text-sm font-medium text-muted-foreground outline-none transition-colors hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring",
                    active && "bg-muted text-foreground"
                  )}
                >
                  <Icon className="h-4 w-4" />
                  {item.label}
                </Link>
              );
            })}
          </nav>
          <Separator className="hidden md:block" />
          <div className="hidden p-3 text-xs text-muted-foreground md:block">
            Evidence synthesis workspace. Backend remains the scientific source of truth.
          </div>
        </aside>
        <main className="min-w-0 p-4 md:p-6">{children}</main>
      </div>
    </div>
  );
}
