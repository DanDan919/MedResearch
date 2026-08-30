# Technical Debt

## Current

- Production migration strategy is not decided. Docker Compose uses config-gated startup migrations for local development only.

## Watch List

- Decide when to add repository/application service abstractions after the first use case makes aggregate loading and saving concrete.
- Decide whether `/health` should become a richer operational health check when more external dependencies exist.
- Run PostgreSQL Testcontainers integration tests in an environment where Docker Desktop is running.
