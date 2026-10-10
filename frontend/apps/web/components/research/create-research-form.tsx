"use client";

import { FormEvent, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Loader2 } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Textarea } from "@medresearch/ui";
import { MedResearchApiError } from "@medresearch/api";
import { useCreateResearch } from "../../lib/api";

const minimumQuestionLength = 12;

export function CreateResearchForm() {
  const router = useRouter();
  const [question, setQuestion] = useState("");
  const [validationError, setValidationError] = useState<string | null>(null);
  const mutation = useCreateResearch();
  const submission = useRef<{ question: string; key: string } | null>(null);
  const inFlight = useRef(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (inFlight.current) return;
    const trimmed = question.trim();
    if (trimmed.length < minimumQuestionLength) {
      setValidationError("Enter a specific research question before starting a run.");
      return;
    }

    setValidationError(null);
    if (submission.current?.question !== trimmed)
      submission.current = { question: trimmed, key: crypto.randomUUID() };
    inFlight.current = true;
    try {
      const response = await mutation.mutateAsync({ question: trimmed, idempotencyKey: submission.current.key });
      router.push(`/research/${response.researchRunId}`);
    } catch {
      // The mutation error is rendered below as the user-facing outcome.
    } finally { inFlight.current = false; }
  }

  const backendError =
    mutation.error instanceof MedResearchApiError
      ? mutation.error.problem?.detail ?? mutation.error.message
      : mutation.error instanceof Error
        ? mutation.error.message
        : null;

  const authenticationRequired = mutation.error instanceof MedResearchApiError && mutation.error.kind === "unauthorized";

  return (
    <Card>
      <CardHeader>
        <CardTitle>New Research Run</CardTitle>
        <CardDescription>
          Submit a bounded scientific question to the real API. Processing remains entirely backend-driven.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form className="space-y-4" onSubmit={onSubmit}>
          <label className="block text-sm font-medium" htmlFor="question">
            Research question
          </label>
          <Textarea
            id="question"
            value={question}
            onChange={(event) => setQuestion(event.target.value)}
            placeholder="Does chronic sleep deprivation impair working memory in adults?"
            rows={6}
            maxLength={1000}
            disabled={mutation.isPending}
          />
          {validationError ? <p className="text-sm text-destructive">{validationError}</p> : null}
          {backendError ? <p className="text-sm text-destructive">{authenticationRequired ? "Sign in to start research." : backendError}</p> : null}
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
            Start Research
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
