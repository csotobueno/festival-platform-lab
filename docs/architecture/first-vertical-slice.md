# First Vertical Slice — Registration and Notifications

**Project:** Festival Platform Lab  
**Stage:** 1 — Modular Monolith Foundation  
**Status:** Proposed design for implementation; notification-status ownership refinement awaits review

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

**Proposed refinement for review:** The initial discussion suggested a `UpdateNotificationStatus` contract on Registration, invoked by Notifications. Prefer Notifications owning its delivery state instead, because otherwise notification state is duplicated or split across modules. If a later use case requires it, Registration or a query endpoint may obtain delivery status through an explicit Notifications read contract.

Concrete interface signatures and project structure are deferred until the implementation issue; contracts will be defined from actual use-case needs.

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

The API must not claim email delivery when it has only accepted or queued a request. The precise response shape and whether a failed send is surfaced as a warning will be chosen during implementation.

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

## 11. Open questions for implementation

1. Exact email normalization and case handling.
2. Exact validation rules and API error/response shape.
3. Whether notification delivery is simulated or performed through an external email provider in Stage 1.
4. Whether delivery should be attempted synchronously after persistence or separated from request acceptance; both must preserve registration correctness.
5. Whether notification failure is returned as a warning, or only surfaced through Notifications' own status.
6. Whether the business ultimately needs audit records for rejected attempts.

These questions do not block accepting the first vertical-slice hypothesis; resolve them just in time when implementing the affected behavior.
