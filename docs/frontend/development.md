# Frontend Development

## Prerequisites

- Node.js 24 or compatible current Node release.
- pnpm 11.
- Rust and Cargo only when running the Tauri desktop shell.

The current local Codex environment had Node and pnpm available, but Rust/Cargo were not available during F1 recovery.

## Install

```bash
cd frontend
pnpm install --frozen-lockfile
```

## Environment

Copy `frontend/.env.example` for local overrides if needed.

```text
NEXT_PUBLIC_MEDRESEARCH_API_URL=http://localhost:8080
VITE_MEDRESEARCH_API_URL=http://localhost:8080
```

These are public frontend URLs, not secrets. Do not place OpenAI keys, database passwords, or provider secrets in `NEXT_PUBLIC_*` or `VITE_*` variables.

## Commands

```bash
pnpm api:generate
pnpm lint
pnpm typecheck
pnpm test
pnpm build
pnpm test:e2e
pnpm desktop:dev
pnpm desktop:build
pnpm desktop:check
```

`pnpm build` builds the web application. Desktop commands require Rust/Cargo because Tauri compiles a Rust shell.

## API Generation

The API package generates TypeScript contracts from:

```text
frontend/packages/api/openapi/medresearch-api.json
```

Run:

```bash
pnpm api:generate
```

Generated code lives in:

```text
frontend/packages/api/src/generated/medresearch-api.ts
```

Do not manually edit generated files except as a temporary recovery step before regenerating.

## Test Policy

Normal frontend tests use mocked API responses. They must not call OpenAI, PubMed, Europe PMC, live full-text endpoints, or arbitrary internet services.

Playwright tests are foundation smoke tests for routing and shell rendering. They do not require a live backend.
