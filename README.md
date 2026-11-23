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
PostgreSQL, and reject duplicate emails. Issue #5 adds an independently testable
Notifications request and delivery lifecycle with PostgreSQL persistence and
simulated delivery. Issue #6 completes Registration → Notifications through a
provider-owned synchronous in-process contract. Notification failures preserve
the committed Registration and its HTTP `201` response.

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
    ├── Notifications.Contracts/   # Public request, result, and service interface
    │   └── Festival.Modules.Notifications.Contracts.csproj
    └── Notifications/
        ├── Festival.Modules.Notifications.csproj
        ├── Domain/
        ├── Application/
        ├── Infrastructure/          # DbContext, simulated delivery, migrations
        └── NotificationsModuleExtensions.cs
```

`Festival.Api` is the composition root. It references both modules and calls
`AddRegistrationModule(connectionString)` and `AddNotificationsModule(connectionString)` to
configure dependency injection. The host also calls `MapRegistrationEndpoints()`
and configures generic problem responses for unhandled exceptions.

Neither implementation references the other. Both reference only
`Festival.Modules.Notifications.Contracts` for integration; that library has no
package, framework, or module dependencies. Registration uses the ASP.NET Core framework
for its module-owned endpoints and EF Core with the Npgsql PostgreSQL provider for
persistence. Notifications uses the same EF Core/Npgsql package versions for its
own persistence, without an ASP.NET Core dependency or public HTTP endpoint.

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
  duplicate-email results. After a successful repository commit, it calls
  `INotificationRequestService` with the Registration identifier and normalized
  email. Domain and Application reference no HTTP, EF Core, or PostgreSQL types.
  Asynchronous operations propagate cancellation tokens.
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
entity or automatic migration on API startup.

## Notifications design

- The internal `Notification` aggregate has a generated `NotificationId`, a
  required nonempty `RegistrationId`, recipient email, UTC `RequestedAt`, and
  initial `Pending` status. Recipient email is trimmed and validated using the
  same basic address checks as Registration; case is preserved because Notifications
  does not use it as a uniqueness key. No Registration record lookup is performed.
- Only `MarkAsSent()` and `MarkAsFailed()` change status. Both require `Pending`;
  `Sent` and `Failed` are terminal, including after loading from persistence.
  There is no transition back to `Pending` and no public state setters.
- `RequestNotificationUseCase` depends on Application-owned
  `INotificationRepository` and `INotificationDelivery`. The aggregate, use case,
  application contracts, and persistence services are internal, accessible only
  to this module and friend test assemblies, including the Registration HTTP
  integration tests for workflow verification. The internal scoped
  `NotificationRequestService` adapter implements the public contract and delegates
  validation and lifecycle rules to this use case. It maps results to
  `NotificationRequestResult.Success`, `DeliveryFailed`, or `InvalidInput`, without
  exposing aggregates or persistence details. Exceptions propagate to the caller.
- The repository's `AddAsync` commits `Pending` and retains the tracked aggregate.
  Delivery then runs with no database transaction open. A separate
  `SaveChangesAsync` commits the terminal status before the use case returns
  `Success(notificationId)` or `DeliveryFailed(notificationId)`. Domain validation
  errors return `InvalidInput(error)` without storage or delivery activity.
- `INotificationDelivery` returns an explicit `Sent` or `Failed` outcome for
  expected provider outcomes, including known rejections/timeouts. The default
  simulated adapter always reports success and sends no email or network request.
  Tests substitute controlled outcomes; there is no failure configuration switch.
- Unexpected adapter errors, cancellation, and all persistence failures propagate.
  There is no broad catch that marks these failures as delivery failures. Initial
  save failure prevents delivery. If delivery throws or the final save fails,
  the previously committed row remains `Pending`; the scope must be disposed
  after an exception rather than reused to retry tracked changes.
- `NotificationDbContext` owns `notifications.notifications` with primary key
  `notification_id`, scalar `registration_id` (no foreign key), `recipient_email`,
  text `status`, and `requested_at` (`timestamptz`). Migration history is in the
  `notifications` schema. The use case, repository, adapter, and context are scoped.

Two commits deliberately leave a reliability gap: a crash or final-save failure
after delivery may leave `Pending` even though delivery occurred. There is no
automatic recovery, retry, idempotency guarantee, or cross-module transaction in
this issue. `Sent` represents simulated delivery success only. Separate schemas
allow both modules to share a physical database without sharing tables or migrations.

## Registration → Notifications integration

The host composes both modules through their existing registration extensions.
`AddNotificationsModule` binds `INotificationRequestService` to its internal adapter
with a scoped lifetime, matching both modules' use cases and EF Core contexts.
Registration invokes it only after `IRegistrationRepository.AddAsync` commits;
invalid input, precheck duplicates, concurrent insert conflicts, and Registration
storage errors never request a notification. Each module accesses its own tables.

Successful simulated delivery stores `Sent`; expected delivery failure stores
`Failed`. Neither changes Registration's `Confirmed` status or the API response.
The contract also reports `InvalidInput` for invalid notification data. Registration
logs any unsuccessful contract outcome and catches unexpected exceptions only
around the notification call, including cancellation after the Registration commit.
Cancellation before/during Registration validation or storage continues to propagate.
The HTTP connection may already be closed if the client cancels after commit.

Integration failures use error event `6001` (`RegistrationConfirmationNotificationFailed`)
with structured `RegistrationId`, `Operation` (`RequestRegistrationConfirmation`),
and `FailureCategory` (`DeliveryFailed`, `InvalidInput`, or `UnexpectedError`).
Unexpected exceptions are attached to the log; recipient emails and names are not
added to log messages or structured fields. Logging-provider failures are isolated
so they cannot turn a committed Registration into an unsuccessful business result.

No transaction spans the two modules. If the initial Notifications save fails or
the process stops between commits, a confirmed Registration may have no notification
record. A final-save failure or unexpected delivery error can leave a committed
`Pending` row, even if simulated delivery already occurred. There are no automatic
retries, recovery, or delivery guarantees. Synchronous delivery also adds to HTTP
request latency. `POST /registrations` still returns only `registrationId` on `201`.

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
export ConnectionStrings__Notifications="$ConnectionStrings__Registration"
dotnet ef database update --project src/Modules/Registration
dotnet ef database update --project src/Modules/Notifications
```

The migrations create each module's schema and table, plus Registration's unique index.
Apply migrations explicitly before starting the API; the database user needs the
corresponding DDL permissions (and permission to create the database if absent).
Both connection strings are required; missing configuration fails fast at startup.

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
dotnet test tests/Festival.Modules.Notifications.Tests
```

Unit tests cover domain validation, value equality, normalization, identity,
confirmed status, UTC timestamp, use-case results, cancellation, and propagation
of unexpected errors. Integration tests exercise the real API host and PostgreSQL
for `201`/`400`/`409`/`500`, persisted data, migration/model consistency, unrelated
database errors, and preservation of `/health`.

Notifications unit tests cover creation, email validation, terminal transitions,
controlled delivery outcomes, operation ordering, cancellation, and exception
propagation. Its PostgreSQL integration tests load `Pending` from a separate
connection during delivery, verify both terminal outcomes in fresh contexts,
update reloaded aggregates, verify DI registration and migration consistency,
and inject real PostgreSQL constraint failures on insert/update. Failed final
saves propagate and preserve the committed `Pending` row. Each Notifications
test uses its own database and the same Testcontainers setup as Registration.

Contract adapter tests verify result mapping and propagation of infrastructure
failures. Registration tests verify notification data and invocation ordering,
failures and cancellation after commit, structured logs, and no notification for
invalid/duplicate/failed Registration attempts. HTTP integration tests migrate both
schemas and verify `Confirmed` with `Sent` or `Failed` in separate module-owned
contexts. Real Notifications insert/update constraint failures preserve HTTP `201`
and generate error logs; initial-save failure leaves no row, final-save failure
leaves `Pending`. Registration database errors still return HTTP `500`.

The concurrency test synchronizes two real repository lookups so both observe
an absent email before either inserts. Separate HTTP scopes/DbContexts then race
against the unique index; exactly one request succeeds and the other conflicts,
with one persisted Registration and one Notification. It uses no timing sleeps or EF InMemory provider.

For older Docker installations that require an explicit API version, prefix the
test command with the daemon's supported version, for example
`DOCKER_API_VERSION=1.43 dotnet test` for Docker 24.

Check migrations against the model after future changes:

```sh
dotnet ef migrations has-pending-model-changes --project src/Modules/Registration
dotnet ef migrations has-pending-model-changes --project src/Modules/Notifications
```

Inspect project references:

```sh
dotnet list src/Festival.Api/Festival.Api.csproj reference
dotnet list src/Modules/Registration/Festival.Modules.Registration.csproj reference
dotnet list src/Modules/Notifications/Festival.Modules.Notifications.csproj reference
dotnet list src/Modules/Notifications.Contracts/Festival.Modules.Notifications.Contracts.csproj reference
```

The API must list both implementation projects. Each module must reference only
Notifications.Contracts; the contracts library must report no project references.
