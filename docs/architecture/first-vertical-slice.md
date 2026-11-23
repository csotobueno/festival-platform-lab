# First Vertical Slice — Registration and Notifications

**Project:** Festival Platform Lab  
**Stage:** 1 — Modular Monolith Foundation  
**Status:** Implemented through Issue #6; Notifications owns its delivery status

## 1. Purpose

Build the smallest complete business workflow that demonstrates clear module boundaries, data ownership, and in-process communication in a .NET modular monolith. This laboratory prioritizes architectural learning while using a plausible festival registration scenario.

## 2. Selected vertical slice

**Workflow: Register an attendee and request a confirmation notification.**

1. A client submits the attendee's name and email address to the Registration API.
2. Registration validates the required fields and email format.
3. Registration checks for an existing registration using a normalized email address.
4. Registration persists a new registration with status `Confirmed`.
5. Registration requests a confirmation notification through an explicit in-process contract.
6. Notifications persists the notification request, attempts delivery (or uses a controlled simulation), and records the delivery outcome.
7. The API returns the registration result independently of email delivery success.

A duplicate or invalid submission is rejected with a meaningful error. It is not persisted as a separate Registration or RegistrationRequest in this slice.

## 3. Scope and minimum rules

### Included

- Name and email are required; values are normalized appropriately.
- Basic email-format validation.
- Email is unique for registrations (using a documented normalization rule).
- A successfully created registration has status `Confirmed`.
- Registration information is persisted in PostgreSQL using EF Core.
- A notification request is persisted and its attempt/result recorded.
- Synchronous in-process module communication through an explicit contract.
- Unit tests for core rules and integration tests for the workflow.

### Not included

- Payment verification or multi-state registration approval.
- Registration-request auditing or persistence of rejected attempts.
- Frontend, authentication, full template management, or real attendee data.
- Message broker, distributed transactions, Outbox, automatic retries, or microservices.
- Real cloud deployment in this slice.

## 4. Module responsibilities and data ownership

| Module | Owns | Responsibilities | Exposed capability |
|---|---|---|---|
| Registration | Registration identity, attendee name/email, registration status | Validate, prevent duplicate registrations, persist, return result | Register an attendee via the host API/application boundary |
| Notifications | Notification request, registration reference, delivery status and attempt details | Receive notification request, persist it, attempt delivery, record result | Request a registration-confirmation notification |

**Important distinction:** `RegistrationStatus` and `NotificationDeliveryStatus` are different concepts. A registration may remain `Confirmed` when a notification is `Failed`.

## 5. Communication and contracts

- The modules execute in one .NET process and are deployed together.
- Registration calls a narrow Notifications contract in-process; it does not access Notifications tables or implementations.
- Notifications receives the minimum details necessary to deliver the message (such as registration identifier, name and email); no direct Registration database access is required.
- The Registration API does not expose Notifications persistence details.
- Notifications does not change Registration's confirmation state.

**Implemented ownership:** Notifications owns its delivery state; no `UpdateNotificationStatus` callback or duplicated delivery state exists in Registration. If a later use case requires it, Registration or a query endpoint may obtain delivery status through an explicit Notifications read contract.

Issue #6 defines `INotificationRequestService.RequestAsync(NotificationRequest,
CancellationToken)` in `Festival.Modules.Notifications.Contracts`, a dependency-free
provider-owned library. The request carries only `RegistrationId` and `RecipientEmail`.
The result is `Success`, `DeliveryFailed`, or `InvalidInput`; no internal entity or
infrastructure details are returned. An internal scoped Notifications adapter calls
the existing use case. Both implementations reference the contracts library and
never reference each other. The API host composes their public DI extensions.

Registration calls the contract only after its repository commits. Expected
unsuccessful outcomes are logged. Unexpected notification errors (including
cancellation after commit) are caught at that call boundary and logged with the
exception, registration identifier, operation and failure category (event `6001`).
No attendee email/name is added to integration log fields. Registration validation
and persistence exceptions continue to propagate. Logging failures cannot invalidate
the committed business result. The API returns HTTP `201` with only `registrationId`,
regardless of notification outcome; no public Notifications endpoint is added.

## 6. Persistence and consistency

- PostgreSQL and EF Core are used locally.
- Each module owns its tables and accesses them only through its own implementation.
- Registration email uniqueness is enforced by a database unique constraint/index as well as application checks, so concurrent requests cannot create duplicates.
- Persist the Registration first; only afterward invoke Notifications. A notification failure must not roll back a committed Registration.
- A synchronous Notifications call does not make the two modules transactionally atomic. A crash after registration commit but before notification-request persistence can result in a confirmed registration with no notification request. This is a **known Stage 1 reliability limitation**, not a solved guarantee.
- When future requirements justify guaranteed notification processing, investigate transactional Outbox, retries and idempotency.

### Minimum outcome scenarios

| Scenario | Registration result | Notifications result |
|---|---|---|
| Valid new attendee, delivery succeeds | `Confirmed` | Request stored; `Sent` (if delivery verified) |
| Invalid name/email | Rejected; no registration stored | Not invoked |
| Duplicate email | Rejected; no new registration stored | Not invoked |
| Delivery provider fails | `Confirmed` | Failure recorded as `Failed` when the request was persisted |
| Crash between registration commit and notification request | `Confirmed` may exist | Request may be missing; explicitly accepted risk for Stage 1 |

The API returns the Registration identifier only and makes no email delivery claim.
Failed notification attempts are observed through Notifications-owned state and
structured logs, without a warning or delivery status in the HTTP response. An
initial notification persistence failure can leave no durable request; a final
save failure may leave `Pending` even after delivery. Neither implies automatic
recovery or guaranteed retry.

## 7. Minimal architectural structure

**Logical structure only — not a final folder or project layout:**

```text
Festival Platform (one deployable host)
  ├── Registration module
  │     ├── registration use case and rules
  │     └── owned persistence
  └── Notifications module
        ├── explicit in-process contract
        ├── notification request and delivery logic
        └── owned persistence
```

Prefer minimum code and abstractions that demonstrate encapsulation. No infrastructure shared across modules should bypass ownership boundaries. A single database server may host module-owned tables; separate physical databases are not necessary in Stage 1.

## 8. Architectural rationale

- **Two modules from Stage 1:** an intentional laboratory decision to practice module boundaries and later extraction. In a smaller production scope, an email service inside Registration might be sufficient.
- **Direct synchronous contract:** simpler than events/message brokers for the first implementation; chosen to learn controlled coupling and testable interactions.
- **Separate delivery state:** Notifications owns the delivery lifecycle; Registration owns registration business status.
- **Minimal domain model:** no speculative payment or accreditation workflows.
- **Evolution, not pre-built distribution:** establish meaningful contracts now but add distributed reliability mechanisms only when the later experiments require them.

## 9. Evolutionary learning path

| Stage | Learning experiment |
|---|---|
| 1 — Modular Monolith Foundation | Build and test Registration + Notifications with explicit in-process boundaries; basic CI and structured logs |
| 2 — Migration Readiness | Measure/inspect coupling and decide whether Notifications should be extracted; assess data and contract boundaries |
| 3 — First Service Extraction | If justified, extract Notifications as a separately deployed service while preserving observable business behavior |
| 4 — Distributed Integration & Resilience | Explore asynchronous messaging, delivery guarantees, Outbox, idempotency, retries, telemetry and tracing |
| 5 — Validation & Architecture Decision | Compare architecture, resilience, performance, operability and cost with evidence |

Payments and Accreditation remain optional future modules/experiments rather than committed Stage 1 work.

## 10. Validation and evidence

- Unit tests validate required fields, normalization and registration rules.
- Integration tests verify PostgreSQL uniqueness and realistic API-to-module flow.
- Tests demonstrate that duplicate submissions do not create extra registrations.
- Tests demonstrate that notification failure does not change registration confirmation.
- Documentation and the resulting PR explain the boundaries and trade-offs.
- GitHub Actions CI will build and run tests once executable projects are created.

## 11. Implementation decisions and remaining questions

Issues #4–#6 implement trimmed, lowercased Registration emails, basic .NET address
validation, HTTP `201`/`400`/`409` with generic `500` responses, simulated synchronous
delivery after persistence, and notification failures isolated from the Registration
response. README documents the concrete rules and test commands.

Whether the business ultimately needs audit records for rejected attempts remains
open and outside this slice. Guaranteed notification processing remains a future
reliability experiment, not a Stage 1 promise.
