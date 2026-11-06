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

The executable foundation is in place: one ASP.NET Core Minimal API host and
independent Registration and Notifications class libraries. Business workflows
remain to be implemented; the domain and architecture documents describe the
planned first vertical slice.

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
    │   └── RegistrationModuleExtensions.cs
    └── Notifications/
        ├── Festival.Modules.Notifications.csproj
        └── NotificationsModuleExtensions.cs
```

`Festival.Api` is the composition root. It references both modules and calls
`AddRegistrationModule()` and `AddNotificationsModule()` to configure dependency
injection. These extension methods currently return the service collection
without registering services, since business functionality is outside this task.

Neither module references the other. Each uses only
`Microsoft.Extensions.DependencyInjection.Abstractions` to expose its registration
extension through `IServiceCollection`, without depending on the ASP.NET Core
framework. There are no inter-module contracts or persistence components yet.

## Build

Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet restore
dotnet build
```

## Run

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

Inspect project references:

```sh
dotnet list src/Festival.Api/Festival.Api.csproj reference
dotnet list src/Modules/Registration/Festival.Modules.Registration.csproj reference
dotnet list src/Modules/Notifications/Festival.Modules.Notifications.csproj reference
```

The API must list both module projects. Each module must report no project
references. No automated test project is included in this foundation; validate
startup and the health endpoint with the commands above.
