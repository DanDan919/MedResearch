# AGENTS.md

MedResearch is an AI-assisted scientific evidence synthesis platform for medical and neuroscience research. It must frame output as evidence synthesis, not diagnosis or treatment advice.

## Start Here

Before significant changes, read:

- `AGENTS.md`
- `ARCHITECTURE.md`
- `docs/development/current-state.md`
- existing ADRs in `docs/architecture/decisions/`

## Development Rules

1. Read `AGENTS.md`, `ARCHITECTURE.md`, and `docs/development/current-state.md` before significant changes.
2. Inspect existing code before creating new abstractions.
3. Prefer modifying existing abstractions over creating duplicate ones.
4. Domain logic must not depend on Infrastructure.
5. Application orchestrates use cases.
6. Infrastructure handles external systems and persistence.
7. API handles HTTP concerns.
8. Do not put business rules in controllers.
9. Do not introduce abstractions without a concrete reason.
10. Use async APIs for I/O.
11. Nullable reference types must remain enabled.
12. Avoid `.Result` and `.Wait()`.
13. Avoid magic strings for domain states.
14. Validate external data before allowing it into trusted domain state.
15. LLM output must NEVER be treated as trusted input.
16. Future LLM structured output must be schema validated.
17. Do not send unrelated persisted data to external LLM providers without explicit approval.
18. Missing scientific data must remain missing/null rather than being guessed.
19. Every scientific claim in the future synthesis layer must be traceable to its source.
20. Medical output must be framed as evidence synthesis, not diagnosis or treatment advice.

## Development Trail

Record notable bugs, architectural problems, surprising behavior, and failed approaches in `docs/development/problems.md`. Do not record every trivial typo.

Use ADRs for significant architectural decisions. If a decision is replaced, mark the old ADR as superseded instead of rewriting history.
