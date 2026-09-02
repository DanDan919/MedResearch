# ADR-011: Harden PubMed E-utilities Adapter With Batching, Rate Limiting, And Opt-In Live Verification

Status: Accepted
Date: 2026-09-02

## Context

PubMed is the first scientific literature source in MedResearch. The adapter already used official NCBI E-utilities, but production use needs clearer compliance with the current NCBI contract: identify the calling tool/contact, keep request rates within documented limits, avoid one request per PMID, retry only transient failures, and keep normal CI independent from live NCBI availability.

The official NCBI E-utilities documentation states that all E-utility requests use the base URL `https://eutils.ncbi.nlm.nih.gov/entrez/eutils/`. ESearch accepts `db` and `term`, can return JSON or XML, and can post results to the History Server with `usehistory=y`. EFetch accepts `db` and either `id` lists or History Server `WebEnv`/`query_key`, with formats controlled by `retmode` and, where applicable, `rettype`. NCBI usage guidance says clients should post no more than three requests per second without an API key; including `api_key` allows up to ten requests per second by default. NCBI encourages/requests `tool` and `email` identification, and recommends batching/History Server for larger retrieval jobs.

Sources consulted:

- NCBI E-utilities usage guidelines and API key: `https://eutilities.github.io/site/API_Key/usageandkey/`
- NCBI E-utilities reference guide: `https://eutilities.github.io/site/Reference_Guide/a_reference/`
- NCBI E-utilities quick start guide: `https://eutilities.github.io/site/Quick_Start/eu_quick/`
- Entrez Programming Utilities Help, History Server: `https://www.ncbi.nlm.nih.gov/books/NBK25497/`
- Entrez Programming Utilities Help, Quick Start: `https://www.ncbi.nlm.nih.gov/books/NBK25500/`

## Decision

Keep Application provider-neutral. PubMed transport details, request parameters, response parsing, rate limiting, retry behavior, identifier normalization, and optional live verification remain in Infrastructure or Infrastructure tests.

Use direct PMID batching for the current bounded retrieval volume:

```text
ESearch db=pubmed retmode=json retmax=MaxResultsPerQuery
  -> distinct PMIDs
  -> EFetch db=pubmed retmode=xml in FetchBatchSize chunks
  -> normalized ScientificStudyCandidate records
```

Do not introduce History Server retrieval yet. Current `MaxResultsPerQuery` is deliberately bounded to at most 200, so direct ID batching keeps the adapter simpler while avoiding one request per PMID. History Server support remains a future-compatible Infrastructure concern and must not leak `WebEnv` or `query_key` into Application contracts.

PubMed options are strongly validated. `ApiKey` is optional. `MaxRequestsPerSecond` defaults to 2 and may not exceed 3 without an API key or 10 with an API key. `Tool` defaults to `MedResearch`; `Email` is optional but validated when configured. Result and batch sizes are bounded. Retry attempts and retry base delay are bounded.

Centralize local request pacing behind `IPubMedRequestGate`, implemented with `System.Threading.RateLimiting.TokenBucketRateLimiter`. ESearch and EFetch share the same gate because they share NCBI E-utilities quota. This limiter is local to one process and is not a distributed quota system.

Use bounded retry only for transient failures: HTTP 429, HTTP 5xx, network transport failures, and HttpClient timeouts. Do not retry HTTP 400, 401/403, malformed successful payloads, URI/configuration validation failures, or deterministic parser/normalization failures. Respect `Retry-After` when present; otherwise use bounded exponential backoff with jitter. Cancellation propagates through rate-limit waiting, HTTP requests, and retry delay.

Add a separate opt-in live smoke test project that is not part of `MedResearch.slnx` and is not run by normal CI. It runs only when explicitly invoked with `MEDRESEARCH_RUN_LIVE_PUBMED_TESTS=true` and contact email configuration. Normal tests continue to use fake HTTP and fixtures only.

## Alternatives Considered

- History Server now: deferred because current retrieval size is bounded and direct batched IDs are simpler to test. This should be revisited when result windows grow beyond small bounded metadata retrieval.
- One EFetch per PMID: rejected because it wastes request quota and conflicts with NCBI batching guidance.
- Retrying every failure: rejected because permanent query/configuration/parser failures should fail fast and visibly.
- Live PubMed in normal CI: rejected because CI should not depend on live internet, DNS, provider availability, or rate-limit state.
- Silent clamping above official rate limits: rejected because fail-fast validation is easier to diagnose than hidden throttling changes.

## Consequences

PubMed production behavior is safer, more deterministic, and closer to NCBI guidance without expanding scientific scope. Normal CI remains secret-free and network-independent for PubMed. Optional live verification exists for explicit external contract checks.

Remaining limitations: rate limiting is per process, not distributed; History Server retrieval is not implemented; live PubMed availability is not continuously verified; PubMed metadata remains incomplete and untrusted; malformed PubMed records without valid PMID/title are skipped rather than assigned synthetic identity.