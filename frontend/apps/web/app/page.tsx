import Link from "next/link";
import { ArrowRight, FileText, Microscope, Server } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@medresearch/ui";

export default function DashboardPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-normal">Dashboard</h1>
        <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
          MedResearch is connected to the existing ASP.NET Core research API. This workspace shows real run creation, research history, run status, report retrieval, and honest gaps where backend APIs do not exist yet.
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Microscope className="h-4 w-4" /> Start a run
            </CardTitle>
            <CardDescription>Submit a research question to the real backend queue.</CardDescription>
          </CardHeader>
          <CardContent>
            <Button asChild>
              <Link href="/research/new">
                New Research <ArrowRight className="h-4 w-4" />
              </Link>
            </Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <FileText className="h-4 w-4" /> Reports
            </CardTitle>
            <CardDescription>Reports are available by run id once the backend has completed synthesis.</CardDescription>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-muted-foreground">A report list API is not exposed yet.</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Server className="h-4 w-4" /> API boundary
            </CardTitle>
            <CardDescription>The frontend presents backend-computed evidence and statistics.</CardDescription>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-muted-foreground">No scientific or statistical calculations run in the UI.</p>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
