import { cn } from "./cn";

export function StatusIndicator({
  tone,
  label,
  className
}: {
  tone: "neutral" | "success" | "warning" | "danger" | "info";
  label: string;
  className?: string;
}) {
  const tones = {
    neutral: "bg-muted-foreground",
    success: "bg-success",
    warning: "bg-warning",
    danger: "bg-destructive",
    info: "bg-primary"
  };

  return (
    <span className={cn("inline-flex items-center gap-2 text-sm text-muted-foreground", className)}>
      <span className={cn("h-2 w-2 rounded-full", tones[tone])} aria-hidden="true" />
      {label}
    </span>
  );
}
