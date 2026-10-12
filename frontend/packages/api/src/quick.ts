import { z } from "zod";
import type { components } from "./generated/medresearch-api";

const id = z.string().uuid();
const text = (max: number) => z.string().min(1).max(max);
const timestamp = z.iso.datetime({ offset: true });
const status = z.enum(["Queued", "Processing", "Completed", "Failed"]);
const citation = z.object({ sourceId: id, excerpt: text(800).min(20) });
const finding = z.object({ text: text(600), citations: citation.array().min(1).max(5) });
const contradiction = z.object({ text: text(600), citations: citation.array().min(2).max(5) });
const answer = z.object({ quickRunId: id, shortAnswer: text(600), findings: finding.array().max(6),
  contradictions: contradiction.array().max(3), limitations: text(500).array().min(1).max(8),
  uncertainty: z.enum(["Limited", "VeryLimited", "Insufficient"]), sourceReferences: id.array().max(5) });
const discovery = z.object({ attemptId: id, provider: z.enum(["PubMed", "EuropePmc"]), providerRecordId: z.string().max(200).nullable() });
export const quickPublicationSchema = z.object({ id, quickRunId: id, title: text(1000), abstract: z.string().max(16000).nullable(),
  pmid: z.string().regex(/^[1-9]\d{0,11}$/).nullable(), pmcid: z.string().regex(/^PMC[1-9]\d{0,11}$/).nullable(), doi: z.string().max(300).nullable(),
  journal: z.string().max(300).nullable(), publicationDate: z.iso.date().nullable(), publicationYear: z.number().int().min(1).max(9999).nullable(),
  authors: text(300).array().max(50), publicationTypes: text(200).array().max(25), discoveries: discovery.array().max(32),
  metadataConflicts: text(100).array().max(10), populationMatch: z.enum(["Unspecified", "MatchedText"]), url: z.string().max(2048).nullable()
}) satisfies z.ZodType<components["schemas"]["QuickPublication"]>;
const attempt = z.object({ id, provider: z.enum(["PubMed", "EuropePmc"]), query: text(512), executedQuery: z.string().max(512).nullable(),
  startedAt: timestamp, finishedAt: timestamp.nullable(), outcome: z.enum(["Started", "SucceededWithResults", "SucceededZeroResults", "Failed", "TimedOut", "OutcomeUnknown", "Disabled"]),
  returnedCount: z.number().int().min(0).max(8).nullable(), failureCategory: z.string().max(64).nullable() });
const retrieval = z.object({ quickRunId: id, queryVersion: text(100), language: z.enum(["ru", "en"]), queries: text(512).array().max(2),
  outcome: z.enum(["Searching", "Selected", "ZeroResults", "NoRelevantSources", "Partial", "Failed", "NeedsClarification"]), isFinal: z.boolean(),
  sources: quickPublicationSchema.array().max(5), attempts: attempt.array().max(4), rejectedCount: z.number().int().nonnegative(), limitations: text(1000).array().max(10) });
const syntheticSource = z.object({ id, quickRunId: id, title: text(300), url: z.string().nullable(), isSynthetic: z.boolean() });
const syntheticStatement = z.object({ text: text(1000), sourceIds: id.array().max(10) });
const syntheticResult = z.object({ quickRunId: id, isSynthetic: z.literal(true), summary: text(2000), facts: syntheticStatement.array().max(10),
  disagreements: syntheticStatement.array().max(10), limitations: text(1000).array().max(10), sources: syntheticSource.array().max(10) });

export const quickResearchDetailsSchema = z.object({ id, question: text(2000), status, createdAt: timestamp, startedAt: timestamp.nullable(),
  completedAt: timestamp.nullable(), safeError: z.string().max(64).nullable(), result: syntheticResult.nullable(), retrieval: retrieval.nullable(),
  answer: answer.nullable(), answerStatus: z.enum(["Started", "Succeeded", "InsufficientEvidence", "Failed", "OutcomeUnknown"]).nullable()
}).superRefine((value, context) => {
  const reject = () => context.addIssue({ code: "custom", message: "Inconsistent Quick result lineage or state" });
  if (value.retrieval && (value.retrieval.quickRunId !== value.id || value.retrieval.sources.some(s => s.quickRunId !== value.id))) reject();
  if (value.result && value.result.quickRunId !== value.id) reject();
  const sources = new Set(value.retrieval?.sources.map(s => s.id));
  if (sources.size !== (value.retrieval?.sources.length ?? 0)) reject();
  if (value.answer) {
    const current = value.answer;
    const citations = [...current.findings.flatMap(f => f.citations), ...current.contradictions.flatMap(c => c.citations)];
    const cited = new Set(citations.map(c => c.sourceId));
    if (current.quickRunId !== value.id || !value.retrieval?.isFinal || value.status !== "Completed" || value.result !== null
      || citations.some(c => !sources.has(c.sourceId)) || current.sourceReferences.some(s => !cited.has(s))
      || cited.size !== current.sourceReferences.length || new Set(current.sourceReferences).size !== current.sourceReferences.length
      || current.contradictions.some(c => new Set(c.citations.map(x => x.sourceId)).size < 2)
      || [...current.findings, ...current.contradictions].some(f => new Set(f.citations.map(c => c.sourceId)).size !== f.citations.length)
      || (current.uncertainty === "Insufficient" ? value.answerStatus !== "InsufficientEvidence" || current.findings.length !== 0 || current.contradictions.length !== 0
        : value.answerStatus !== "Succeeded" || !current.findings.length || current.shortAnswer !== current.findings[0].text)) reject();
  } else if (value.answerStatus === "Succeeded" || value.answerStatus === "InsufficientEvidence") reject();
  if ((value.status === "Queued" || value.status === "Processing") && value.completedAt !== null) reject();
}) satisfies z.ZodType<components["schemas"]["QuickResearchDetails"]>;

export const quickResearchHistorySchema = z.object({ items: z.object({ id, question: text(2000), status, createdAt: timestamp,
  completedAt: timestamp.nullable() }).array().max(50), page: z.number().int().min(1).max(100000), pageSize: z.number().int().min(1).max(50), totalCount: z.number().int().nonnegative() }) satisfies z.ZodType<components["schemas"]["QuickResearchHistory"]>;
export type QuickResearchDetails = z.infer<typeof quickResearchDetailsSchema>;
export type QuickResearchHistory = z.infer<typeof quickResearchHistorySchema>;
export type QuickPublication = z.infer<typeof quickPublicationSchema>;

export const quickQueryKeys = {
  detail: (sessionId: string | null, runId: string) => ["quick-research", sessionId, runId] as const,
  history: (sessionId: string | null, page: number) => ["quick-research", sessionId, "history", page] as const
};

// Validate the API's link against its reported identifier; never invent a missing URL.
export function safeQuickPublicationUrl(source: QuickPublication): string | null {
  if (!source.url) return null;
  try {
    const url = new URL(source.url);
    if (url.protocol !== "https:" || url.username || url.password || url.port || url.search || url.hash) return null;
    const path = decodeURIComponent(url.pathname);
    const matches = url.hostname === "pubmed.ncbi.nlm.nih.gov" && source.pmid && path === `/${source.pmid}/`
      || url.hostname === "europepmc.org" && source.pmcid && path === `/articles/${source.pmcid}`
      || url.hostname === "doi.org" && source.doi && path.slice(1) === source.doi;
    return matches ? url.href : null;
  } catch { return null; }
}
