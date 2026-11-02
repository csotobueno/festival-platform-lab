# Festival Business Capabilities and Candidate Context Boundaries

**Project:** Festival Platform Lab  
**Stage:** 1 — Modular Monolith Foundation  
**Status:** Initial domain-discovery hypothesis (not final architecture)

## 1. Purpose and scope

Identify the festival's major business capabilities, their responsibilities, actors, data ownership candidates, dependencies, and candidate bounded-context boundaries before selecting a first implementation flow.

The laboratory models a five-day festival with approximately 5,000 attendees. Current activities include registration through external forms, externally processed payments with vouchers, attendee accreditation, daily seating assignment, notifications, possible access control, and additional activities.

This document describes the **business**, not the final .NET project structure. A business capability is not automatically a bounded context, a module, or a microservice.

## 2. Business capabilities

| Capability | Purpose and responsibility | Main actors (initial hypothesis) | Important concepts | Data ownership candidate | Dependencies / business events (candidates) |
|---|---|---|---|---|---|
| **Registration** | Record registrations, maintain registration status and determine whether registration requirements are satisfied. | Attendee, registration staff | Registration, registration status, attendee registration details | Registration records and their lifecycle | May consume confirmed payment information; *RegistrationSubmitted*, *RegistrationConfirmed*, *RegistrationCancelled* |
| **Payments** | Track payments and supporting evidence; record and communicate payment outcomes. | Attendee, finance staff, external payment provider | Payment, payment reference, voucher, payment status | Payment records, evidence references and payment confirmation state | Relates payments to registrations; *PaymentConfirmed*, *PaymentRejected* |
| **Accreditation** | Verify eligibility and record accreditation during festival operations. | Accreditation operator, attendee | Accreditation, accreditation status, eligibility verification | Accreditation records and their lifecycle | Reads registration eligibility through a contract; *AttendeeAccredited* |
| **Notifications** | Receive notification requests and manage delivery if tracking, failures, or retries are required. | Business modules, attendee, delivery provider | Notification request, channel, delivery status, template | Notification requests and delivery records, if implemented | Receives requests from Registration, Payments, Accreditation; *NotificationSent*, *NotificationFailed* |
| **Access Control** | Validate permission to enter festival facilities, if required. | Gate operator, attendee | Entry authorization, access attempt, entry record | Access attempts and entry decisions | May depend on accreditation; *EntryAuthorized*, *EntryDenied* |
| **Activities** | Coordinate additional festival activities, if digital management is needed. | Activity organizer, attendee | Activity, session, enrollment | Activity catalog and registrations, if implemented | May depend on attendee identity/eligibility; *ActivityEnrollmentCreated* |
| **Assignment (external)** | Allocate daily seating positions fairly; managed by a separate project. | Attendee, festival operator | Assignment request, assignment group, festival day, spot | Owned by `festival-assignment-system`, not this lab | Potential future explicit integration; **no integration planned in Stage 1** |

> **Interpretation:** Actors, ownership details and event names above are **candidate modeling inputs**, not validated requirements or committed integration contracts. In particular, publication of events is **not** a Stage 1 implementation decision.

## 3. Initial boundaries and ownership hypotheses

### Registration
- Owns the registration lifecycle and applies rules for changing registration state.
- Other capabilities must not update its tables or state directly.
- A successful payment may be an input to confirmation, but Registration decides whether its own rules permit the transition.

### Payments
- Owns payment verification, payment records and payment outcomes.
- Does not directly mark a registration as confirmed.
- Can communicate a confirmed payment through an explicit contract; whether this is an in-process call or event remains open.

### Accreditation
- Owns accreditation decisions and records.
- Does not directly query Registration-owned tables.
- For the initial modular monolith, we **prefer an explicit synchronous, in-process contract** to check current registration eligibility and retrieve identification data rather than copying attendee names into Accreditation.
- This preference must be revisited if throughput, availability or consistency requirements justify a local read model.

### Notifications
- A dedicated module is a reasonable candidate **if** notifications have meaningful delivery state, history, or retry responsibilities.
- Originating modules determine **when and why** a business notification is needed; Notifications handles **request processing and delivery**.
- A simple reusable email adapter might be sufficient if notification management proves to be only a technical concern. The final bounded-context boundary is not yet established.
- For the first simple flow, a direct in-process request is preferred over introducing an event bus without need; failure handling is still undecided.

### Access Control and Activities
- Recognized business capabilities, but currently too little information exists to define their boundaries or business rules confidently.
- They are not required in the first Stage 1 functional slice.

### Assignment
- External capability owned by the independent `festival-assignment-system` repository.
- Do not replicate its assignment engine or establish a technical integration in Stage 1.

## 4. Candidate context map

```text
Payments ── payment outcome ──> Registration
                                    │
                                    ├── eligibility / identity contract ──> Accreditation
                                    │
                                    └── notification request ──> Notifications

Payments ── notification request ─────────────> Notifications
Accreditation ── notification request ───────> Notifications

Access Control  ··· potential eligibility dependency ··· Accreditation
Activities      ··· dependencies to be discovered
Assignment      [external capability; no Stage 1 integration]
```

Arrows show **information or service dependency**, not necessarily a direct code dependency, message broker, or HTTP call. The exact contract direction and invocation model will be refined during solution design.

## 5. Initial integration principles

1. **Explicit boundaries:** Each implemented module exposes a narrow contract rather than allowing other modules to manipulate its internal state or tables.
2. **Business ownership:** The owner of a business lifecycle decides its valid state transitions.
3. **Pragmatic communication:** Prefer synchronous, in-process integration for straightforward Stage 1 requirements; event-driven communication remains an option when justified.
4. **No implicit distributed guarantees:** A synchronous eligibility check reads current state at check time but does **not** ensure the result remains valid across concurrent operations (time-of-check/time-of-use risk).
5. **No premature duplication:** Keep Registration-owned names and eligibility data in Registration initially; reconsider projections based on measured read load and operational needs.
6. **Business outcome vs delivery outcome:** Registration success and notification delivery success are distinct. The retry/failure policy is not yet decided.
7. **Evolution over speculation:** Revisit these decisions when evaluating service extraction, independent data stores, availability and eventual consistency.

## 6. Candidate first vertical slice

**Registration → Notifications** is a promising first functional flow:

1. Submit a registration.
2. Validate and persist Registration-owned data.
3. Request a confirmation notification through an explicit module contract.
4. Observe the resulting success/failure behavior and test both modules.

This is a **candidate**, not a final selection. Registration → Accreditation may offer a more meaningful boundary exercise depending on the requirements discovered next.

## 7. Open questions and uncertainties

1. **Registration source of truth:** Will the laboratory model the real external forms/Excel import process, or deliberately simulate direct Registration API creation?
2. **Registration eligibility:** Which rules actually make a registration valid or confirmed? Is payment mandatory, and is it manually verified?
3. **Cancellation:** Can a registration be cancelled after accreditation, and should accreditation be revoked automatically?
4. **Identity:** What identifier is shared between capabilities, and which module is authoritative for personal information?
5. **Payments:** What statuses, verification steps and business rules are necessary for the laboratory?
6. **Notifications:** Are delivery state, retries and history actual requirements or deliberate experimental features? What should happen if sending fails after Registration succeeds?
7. **Operational load:** How frequently will Accreditation query Registration during peak hours, and what happens if a registration changes concurrently?
8. **Access Control / Activities:** Which workflows, rules and authoritative records are needed, if any?
9. **First implementation:** Which two-module flow delivers the best balance of domain learning and manageable complexity?

These questions do **not** block documenting the initial map. They should be resolved incrementally when the corresponding functionality enters scope.

## 8. Stage 1 decision status

| Topic | Status |
|---|---|
| Begin with a modular monolith, not microservices | **Established project strategy** |
| Keep `festival-assignment-system` independent | **Established project boundary** |
| Registration and Accreditation are distinct bounded-context candidates | **Working hypothesis** |
| Modules own their data and protect their business state | **Design principle** |
| Accreditation queries Registration through an explicit in-process contract initially | **Preferred Stage 1 hypothesis** |
| Payments communicates outcomes; Registration decides confirmation | **Preferred responsibility split** |
| Notifications as a separate bounded context | **Unconfirmed; candidate module** |
| Direct notification request vs domain event | **Direct call preferred for initial simplicity, not finalized** |
| Notification failure policy | **Open** |
| First implemented flow and module structure | **Open** |

## 9. Next steps

1. Review this document for business inaccuracies and unclear boundaries.
2. Treat it as the evidence artifact for the first domain-discovery issue.
3. Select the first minimal vertical slice and define its invariants and contracts.
4. Build the initial .NET 10 modular monolith and write tests, refining this map as evidence emerges.

**Guiding principle:** Discover enough domain structure to implement safely; learn and refine through working software rather than attempting to finalize the whole model in advance.
