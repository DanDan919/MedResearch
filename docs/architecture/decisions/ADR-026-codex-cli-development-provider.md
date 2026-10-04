# ADR-026: Codex CLI development-only structured LLM provider

## Status

Accepted

## Context

MedResearch has a provider-neutral `IStructuredLlmClient` and an OpenAI API
adapter. A real, authenticated development run is useful for exercising the
existing planner, extractor, evaluator, and synthesizer contracts without
requiring an `OPENAI_API_KEY`. The installed Codex CLI can run non-interactively
with strict JSON Schema output.

## Decision

Add `CodexCliStructuredLlmClient` in Infrastructure as a development/manual-E2E
implementation of the existing port. It launches `codex exec` through a small
process-runner abstraction with `ArgumentList`, stdin prompt transport,
`--output-schema`, `--output-last-message`, a unique temporary working
directory, and `read-only` sandbox mode. Timeout and cancellation kill only the
created process tree. Per-request schema/output files are temporary and are
cleaned after the call.

`AI:Provider=CodexCli` is accepted only in `Development` or
`ManualScientificE2E`; production configuration fails closed. The adapter does
not inspect, copy, log, or persist Codex/ChatGPT credentials. It does not enable
web search and instructs the agent to use only MedResearch-supplied context.

## Consequences

- Application stages do not branch on provider identity.
- Normal tests and CI remain deterministic and do not consume Codex allowance.
- Codex CLI is a local process, not proof of offline/local inference; model
  inference may be remote through the user's authenticated account.
- The current CLI does not provide a separately verified per-request tool
  disable switch, so repository isolation, read-only execution, no `--search`,
  and explicit prompt restrictions remain the available boundary.
- Scientific grounding, deterministic validation, PostgreSQL provenance, and
  quantitative calculations remain owned by MedResearch rather than Codex.
