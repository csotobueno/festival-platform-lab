## Project Context

Festival Platform Lab is a software architecture learning project focused on evolving a modular monolith into distributed services.

The primary objective is to develop architectural understanding through incremental implementation, automated testing, and evidence-based decisions.

## Technology Stack

- .NET 10 / C#
- ASP.NET Core
- PostgreSQL and Entity Framework Core (when persistence is introduced)
- xUnit
- Docker (when required)
- GitHub Actions (when introduced)

Do not introduce technologies before the relevant issue requires them.

## Architectural Principles

- Follow a modular monolith architecture during Stage 1.
- Keep module responsibilities and boundaries explicit.
- Each module owns its business logic and data.
- Avoid direct dependencies between module implementations.
- Expose module capabilities through explicit contracts.
- Use the API host as the Composition Root.
- Apply DDD pragmatically; avoid unnecessary abstractions.
- Do not introduce distributed infrastructure prematurely.
- Favor simple, maintainable solutions over speculative extensibility.

## Development Workflow

- Work only within the scope of the assigned GitHub issue.
- Review relevant documentation before implementing changes.
- Implement the smallest solution that satisfies the acceptance criteria.
- Do not introduce unrelated refactoring or dependencies.
- Preserve existing behavior unless explicitly instructed otherwise.
- Add or update automated tests when behavior changes.
- Run applicable build and test commands before completing the task.
- Report tests that could not be executed.

## Code Quality

- Use meaningful names and consistent C# conventions.
- Prefer explicit dependencies and constructor injection.
- Respect module encapsulation.
- Keep business rules inside their owning modules.
- Avoid premature shared infrastructure and generic abstractions.
- Do not introduce unnecessary design patterns.

## Agent Responsibilities

Before implementation:
- Inspect the relevant existing code and documentation.
- Identify architectural constraints and possible conflicts.
- Follow the approved issue scope.

After implementation:
- Summarize files created or modified.
- Explain significant implementation decisions.
- Report build and test results.
- Identify risks, assumptions, and unresolved questions.
- Highlight any deviation from the original issue.

## Restrictions

- Do not create commits or Pull Requests unless explicitly requested.
- Do not modify unrelated files.
- Do not add external dependencies without justification.
- Do not modify architectural boundaries without approval.
- Do not introduce credentials or sensitive data into the repository.
- Do not silently resolve significant architectural ambiguities.

## Reference Documentation

Consult these documents when relevant and available:

- `README.md`
- `docs/domain/business-capabilities.md`
- `docs/architecture/first-vertical-slice.md`

The existing project documentation takes precedence over assumptions about future architecture.