import * as React from "react";
import { cn } from "./cn";

export function Badge({
  className,
  tone = "neutral",
  ...props
}: React.HTMLAttributes<HTMLSpanElement> & {
  tone?: "neutral" | "success" | "warning" | "danger" | "info";
}) {
  const tones = {
    neutral: "border-border bg-muted text-muted-foreground",
    success: "border-success/30 bg-success/10 text-success",
    warning: "border-warning/30 bg-warning/10 text-warning",
    danger: "border-destructive/30 bg-destructive/10 text-destructive",
    info: "border-primary/30 bg-primary/10 text-primary"
  };

  return (
    <span
      className={cn("inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium", tones[tone], className)}
      {...props}
    />
  );
}
