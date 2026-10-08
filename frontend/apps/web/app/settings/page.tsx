import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";

export default function SettingsPage() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Settings</CardTitle>
        <CardDescription>Runtime configuration is currently supplied through environment variables.</CardDescription>
      </CardHeader>
      <CardContent className="text-sm text-muted-foreground">
        Browser requests use the same-origin authenticated session. Runtime settings are managed by the operator.
      </CardContent>
    </Card>
  );
}
