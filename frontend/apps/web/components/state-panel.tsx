import { AlertCircle, Loader2 } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";

export function LoadingPanel({ title = "Loading" }: { title?: string }) {
  return (
    <Card>
      <CardContent className="flex items-center gap-3 py-6 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" />
        {title}
      </CardContent>
    </Card>
  );
}

export function EmptyState({
  title,
  description
}: {
  title: string;
  description: string;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
    </Card>
  );
}

export function ErrorPanel({ title, message }: { title: string; message: string }) {
  return (
    <Card role="alert" className="border-destructive">
      <CardContent className="flex items-start gap-3 py-6">
        <AlertCircle className="mt-0.5 h-4 w-4 text-destructive" />
        <div>
          <div className="text-sm font-medium">{title}</div>
          <p className="mt-1 text-sm text-muted-foreground">{message}</p>
        </div>
      </CardContent>
    </Card>
  );
}
