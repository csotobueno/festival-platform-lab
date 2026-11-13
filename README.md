# Festival Platform Lab

Experimental software architecture lab based on a real-world festival domain.

## Purpose

Explore the evolution from a modular monolith toward distributed services,
focusing on architectural reasoning, incremental migration and technical evidence.

## Learning goals

- Modular monoliths
- Domain-Driven Design and bounded contexts
- Incremental migration toward microservices
- Strangler Fig
- Event-driven integration
- Resilience and observability
- Architectural trade-offs

## Architecture journey

1. Modular Monolith Foundation
2. Migration Readiness
3. First Service Extraction
4. Distributed Integration & Resilience
5. Validation & Architecture Decision

## Status

Stage 1 — Modular Monolith Foundation

Issue #4 implements the Registration portion of the first vertical slice:
validate attendee details, create a confirmed registration, persist it in
PostgreSQL, and reject duplicate emails. Notifications remains an independent
placeholder. The domain and architecture documents describe the broader planned
flow; notification integration is deliberately deferred by this issue.

## Solution structure

```text
Festival.Platform.slnx
src/
├── Festival.Api/
│   ├── Festival.Api.csproj
│   └── Program.cs
└── Modules/
    ├── Registration/
    │   ├── Festival.Modules.Registration.csproj
    │   ├── Domain/
    │   ├── Application/
    │   ├── Infrastructure/          # DbContext, repository, migrations
    │   ├── Presentation/            # POST /registrations
    │   └── RegistrationModuleExtensions.cs
    └── Notifications/
        ├── Festival.Modules.Notifications.csproj
        └── NotificationsModuleExtensions.cs
```

`Festival.Api` is the composition root. It references both modules and calls
`AddRegistrationModule(connectionString)` and `AddNotificationsModule()` to
configure dependency injection. The host also calls `MapRegistrationEndpoints()`
and configures generic problem responses for unhandled exceptions.

Neither module references the other. Registration uses the ASP.NET Core framework
for its module-owned endpoints and EF Core with the Npgsql PostgreSQL provider for
persistence. Notifications retains only its DI abstraction dependency.

## Registration design

- `Registration` is the only aggregate: a generated `Guid` technical identity,
  immutable attendee details, `Confirmed` status, and a UTC `DateTimeOffset`.
  A private constructor and read-only public state prevent invalid construction
  or unsupported lifecycle changes through the public API.
- `AttendeeDetails` is an immutable record with value equality. Name and email
  are required and trimmed. Email is lowercased using `ToLowerInvariant()`;
  the normalized value is the business uniqueness key for the single edition.
  Basic validation uses .NET `MailAddress.TryCreate`, rejects whitespace and
  display-name syntax, and requires an address rather than a list. It does not
  verify DNS, mailbox ownership, or provider-specific aliases/dot rules.
  Lowercasing the entire address is a deliberate Stage 1 business rule.
- `RegisterAttendeeUseCase` depends on the Application-owned
  `IRegistrationRepository`, with explicit success, invalid-input, and
  duplicate-email results. Domain and Application reference no HTTP, EF Core,
  or PostgreSQL types. Asynchronous operations propagate cancellation tokens.
- The internal `RegistrationDbContext` and `EfRegistrationRepository` implement
  persistence. Only the integration test assembly can access these internals.
  The use case, repository, and DbContext have scoped lifetimes.
- The aggregate and its owned value object map to one table,
  `registration.registrations`: `registration_id` (`uuid`, primary key), `name`,
  normalized `email`, `status` (text), and `registered_at` (`timestamptz`).
  EF migration history is also owned by the `registration` schema.
- The application lookup gives an early conflict; the unique index
  `ux_registrations_email` enforces correctness if concurrent lookups both pass.
  The repository translates only PostgreSQL error `23505` for that specific
  schema/table/index into a duplicate result. Other failures propagate to the
  host's generic HTTP 500 handler. A failed duplicate insert is detached so a
  later operation in the same scope cannot retry it accidentally.

One module project keeps logical layers simple, with no generic repository or
Unit of Work. Layering inside that project is maintained by code organization;
it is not enforced by separate assemblies. There is no independent Attendee
entity, notifications integration, or automatic migration on API startup.

## Build

Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet restore
dotnet build
dotnet tool restore
```

## Run

Use a local PostgreSQL instance and supply its connection string through host
configuration. No credentials are committed. For example, replace the placeholders
in your terminal (the environment variable also configures the EF design factory):

```sh
export ConnectionStrings__Registration='Host=localhost;Port=5432;Database=festival;Username=<user>;Password=<password>'
dotnet ef database update --project src/Modules/Registration
```

The initial migration creates the module-owned schema, table, and unique index.
Apply migrations explicitly before starting the API; the database user needs the
corresponding DDL permissions (and permission to create the database if absent).
Missing connection-string configuration fails fast at startup.

Start the API with an explicit local URL:

```sh
dotnet run --project src/Festival.Api -- --urls http://localhost:5000
```

Stop it with `Ctrl+C`.

## Validate

With the API running, execute in another terminal:

```sh
curl --fail --include http://localhost:5000/health
```

Expected result: HTTP `200 OK`. This endpoint verifies that the host can respond;
it does not check external dependencies.

Register an attendee:

```sh
curl --include http://localhost:5000/registrations \
  --header 'Content-Type: application/json' \
  --data '{"name":"Jane Doe","email":"jane@example.com"}'
```

Expected HTTP `201 Created`, with a generated identifier, for example:

```json
{"registrationId":"6cf49950-8d7f-4091-955b-cbe966698f36"}
```

No `Location` header is supplied because there is no retrieval endpoint in this
issue. Repeating the request with ` JANE@EXAMPLE.COM ` returns HTTP `409 Conflict`:

```json
{
  "title": "Email already registered",
  "status": 409,
  "detail": "A registration already exists for this email address."
}
```

Missing/blank name or email, or an invalid email, returns HTTP `400 Bad Request`
with a problem response describing the validation failure. For example, email
`invalid` produces these fields (standard problem metadata is omitted here):

```json
{
  "title": "Invalid registration",
  "status": 400,
  "detail": "Email must be a valid email address. (Parameter 'email')"
}
```

Unexpected failures return a generic HTTP `500` problem response without exception
or database details, including in Development. Invalid JSON and null request
bodies also return `400` through ASP.NET Core request binding.

## Automated tests

Docker must be running for integration tests. Testcontainers starts PostgreSQL
`17.6` with temporary credentials and a random port, applies the actual migrations,
and creates a separate database for every test. It removes each database and the
container afterward; no externally configured connection string is needed.

```sh
dotnet test
```

To run only unit tests without Docker:

```sh
dotnet test tests/Festival.Modules.Registration.Tests
```

Unit tests cover domain validation, value equality, normalization, identity,
confirmed status, UTC timestamp, use-case results, cancellation, and propagation
of unexpected errors. Integration tests exercise the real API host and PostgreSQL
for `201`/`400`/`409`/`500`, persisted data, migration/model consistency, unrelated
database errors, and preservation of `/health`.

The concurrency test synchronizes two real repository lookups so both observe
an absent email before either inserts. Separate HTTP scopes/DbContexts then race
against the unique index; exactly one request succeeds and the other conflicts,
with one persisted row. It uses no timing sleeps or EF InMemory provider.

For older Docker installations that require an explicit API version, prefix the
test command with the daemon's supported version, for example
`DOCKER_API_VERSION=1.43 dotnet test` for Docker 24.

Check migrations against the model after future changes:

```sh
dotnet ef migrations has-pending-model-changes --project src/Modules/Registration
```

Inspect project references:

```sh
dotnet list src/Festival.Api/Festival.Api.csproj reference
dotnet list src/Modules/Registration/Festival.Modules.Registration.csproj reference
dotnet list src/Modules/Notifications/Festival.Modules.Notifications.csproj reference
```

The API must list both module projects. Each module must report no project
references.
