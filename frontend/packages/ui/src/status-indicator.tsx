import { cn } from "./cn";

export function StatusIndicator({
  tone,
  label,
  className,
  compact = false
}: {
  tone: "neutral" | "success" | "warning" | "danger" | "info";
  label: string;
  className?: string;
  compact?: boolean;
}) {
  const tones = {
    neutral: "bg-muted-foreground",
    success: "bg-success",
    warning: "bg-warning",
    danger: "bg-destructive",
    info: "bg-primary"
  };

  return (
    <span role="status" aria-live="polite" title={label} className={cn("inline-flex items-center gap-2 text-sm text-muted-foreground", className)}>
      <span className={cn("h-2 w-2 rounded-full", tones[tone])} aria-hidden="true" />
      <span className={compact ? "sr-only sm:not-sr-only" : undefined}>{label}</span>
    </span>
  );
}
