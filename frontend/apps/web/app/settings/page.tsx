import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";

export default function SettingsPage() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Settings</CardTitle>
        <CardDescription>Runtime configuration is currently supplied through environment variables.</CardDescription>
      </CardHeader>
      <CardContent className="text-sm text-muted-foreground">
        Configure the API URL with <code>NEXT_PUBLIC_MEDRESEARCH_API_URL</code> for web and{" "}
        <code>VITE_MEDRESEARCH_API_URL</code> for desktop.
      </CardContent>
    </Card>
  );
}
