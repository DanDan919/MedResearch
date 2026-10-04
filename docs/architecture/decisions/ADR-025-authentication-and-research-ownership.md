# ADR-025: Authentication and Research Ownership Boundary

- Status: Accepted
- Date: 2026-10-04

## Context

Before F10, the API was suitable for local development but had no authenticated
principal or resource authorization. Research routes loaded resources by UUID
alone, so a product boundary could not prevent cross-user research access or
owner-count leakage through pagination.

## Decision

Use ASP.NET Core JWT Bearer validation as the production authentication
mechanism. The API consumes an external identity provider and validates issuer,
audience, signature, and lifetime through the framework. The immutable `sub`
claim is normalized as the application actor identity; missing, blank, or
oversized subjects fail closed. MedResearch does not issue tokens or store
passwords.

Use a clearly explicit `DevelopmentLocal` authentication mode only when the
host environment is `Development`. It supplies the deterministic
`local-development-user` subject for local Compose/browser work. CI tests use a
test-only authentication handler in the test project, so no identity provider
or production bypass is shipped for test use.

Persist `ResearchQuestion.OwnerSubjectId` as the immutable ownership root.
`ResearchRun` and run-scoped scientific data inherit access through the
question relationship. Protected EF queries apply the owner predicate before
pagination or materialization. A foreign/nonexistent run returns the same
404-style result after authentication. Health endpoints remain anonymous.

Existing rows are migrated to the explicit `legacy-unowned` subject. They are
not assigned to whichever caller first authenticates and remain inaccessible
until an explicit future migration/ownership operation exists.

## Consequences

- Authentication and authorization are separate from worker lease/fencing.
  Workers continue under trusted system execution and must still satisfy the
  existing lease/version fence.
- Canonical Study and reusable SourceMaterial identity remain global; ownership
  is not duplicated onto those scientific entities.
- Bearer transport does not create a browser cookie CSRF surface. Token
  persistence, refresh, collaboration, quotas, and ownership transfer are
  deliberately deferred.
- A trusted issuer and audience must be supplied in production deployment.
