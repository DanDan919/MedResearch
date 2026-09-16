# ADR-015: Keep Live Scientific E2E Validation Explicit And Outside Normal CI

Date: 2026-09-16

## Status

Accepted

## Context

MedResearch normal tests already prove deterministic behavior with fake LLM/providers and PostgreSQL/Testcontainers in CI. Milestone 16 needs a way to validate the real pipeline against live scientific providers and live OpenAI, but normal CI must remain deterministic, secret-free, network-independent, and free of paid API calls. Live validation also writes to PostgreSQL, so it must never run against an unknown or production database by accident.

## Decision

Add an optional live E2E validation project outside `MedResearch.slnx`: `tests/MedResearch.LiveE2EValidationTests`. The harness uses the real API composition root, production dependency injection, hosted background worker, PostgreSQL persistence, OpenAI structured generation, PubMed, Europe PMC, source acquisition, extraction, evaluation, synthesis, and report endpoint. It submits a normal HTTP research request instead of directly invoking provider adapters as a substitute for E2E.

The project is skipped unless `MEDRESEARCH_RUN_LIVE_E2E=true` is set. It also requires an isolated PostgreSQL connection string, `MEDRESEARCH_LIVE_E2E_DATABASE_ACK=isolated`, OpenAI model/API key configuration, and PubMed contact email. Runtime limits are overridden to keep the validation deliberately small. Normal solution tests and GitHub Actions do not run this project.

Add `ResearchPlanning:MaxSearchQueries` so live validation can cap planner fan-out through normal configuration while preserving the default maximum of 5.

## Consequences

The system now has a safe manual path for real provider validation without weakening deterministic CI. Live success proves one bounded real-data path at a point in time; it does not prove provider uptime, scientific completeness, or production readiness for arbitrary questions. Secrets remain environment-supplied and are not committed.
