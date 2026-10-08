<!--
Sync Impact Report — temporary adoption review material; remove before committing.
Version change: unratified template → 1.0.0 (initial adoption).
Modified principles: five unfilled template slots replaced by Principles I–VII.
Added sections: Technical and Repository Constraints; Development Workflow and Quality Gates;
completed Governance and adoption metadata.
Removed sections: none; illustrative comments and placeholders removed.
Baseline read completely: SRS_Authentication_API_v1.1.md (v1.1),
TECHNICAL_CONSTRAINTS.md (v1.0), ROADMAP_SPECKIT_AUTH_API_v1.1.md (v1.1).
Discrepancy: Roadmap §7.2 and SRS §46 call the 15-minute access-token lifetime recommended;
SRS FR-JWT-009 requires it as the configurable default. The explicit requirement governs.
No unresolved project decisions or deferred placeholders block constitution adoption.
Synchronization follow-up (reported only; templates and commands remain unchanged):
- tasks-template.md and speckit-tasks guidance call tests optional; generated artifacts MUST
  include tests required by Principle VI and the active roadmap gate.
- spec-template.md and speckit-specify guidance permit reasonable defaults; generated artifacts
  MUST NOT promote assumptions into requirements or invent missing baseline decisions.
- plan-template.md sample layouts, technologies, metrics and Complexity Tracking examples
  MUST NOT override prescribed projects, stack, scope or baseline amendment governance.
- Generic plan/task phase labels are workflow subdivisions, not the eight roadmap phases;
  generated artifacts MUST identify their active roadmap phase and gate explicitly.
No baseline documents, template sources, feature artifacts or application code modified.
-->

# Authentication API Constitution

## Core Principles

### I. Baseline Authority and Traceability

All project work and Spec Kit artifacts MUST obey this source-of-truth hierarchy, in order:

1. `./baseline/SRS_Authentication_API_v1.1.md`: product behavior, functional and security
   requirements, external contracts and product scope.
2. `./baseline/TECHNICAL_CONSTRAINTS.md`: architecture, technologies, implementation constraints,
   engineering standards, testing technologies, authorized and prohibited dependencies.
3. `./baseline/ROADMAP_SPECKIT_AUTH_API_v1.1.md`: implementation order, phase boundaries,
   inter-phase dependencies and completion gates.
4. Active feature `spec.md`: current feature requirements within the normative baseline.
5. Active feature `plan.md`: implementation approach consistent with all higher sources.
6. Active feature `tasks.md`: executable work consistent with the specification and plan.

The mandatory precedence is **SRS > Technical Constraints > Roadmap > spec.md > plan.md >
tasks.md**. This constitution MUST govern adherence to that hierarchy and MUST NOT replace
or override the normative baseline. Lower sources MUST NOT reinterpret, weaken, override or
silently contradict higher sources. Explicit identified SRS requirements MUST NOT be weakened
by descriptive summaries: FR-JWT-009 establishes a mandatory configurable default despite
the word “recommended” in summary/roadmap text.

Product requirements MUST be traceable to SRS identifiers; architecture and technology
decisions MUST be traceable to technical-constraint sections; sequencing and gates MUST be
traceable to the roadmap. Artifacts MUST distinguish baseline requirements, established
technical decisions, phase-specific implementation decisions and unresolved questions.
Missing information MUST NOT be filled by inventing requirements, technologies, architecture,
infrastructure, abstractions or future capabilities. Choices within the baseline MUST be
recorded as implementation choices, not promoted into normative requirements.

Material contradictions MUST be reported with their sources and resolved using precedence.
If precedence does not resolve a material ambiguity, affected work MUST stop for an explicit
project decision. Baseline files MUST NOT be silently modified or automatically rewritten
by specification, planning, task generation or implementation workflows.

Rationale: baseline authority prevents generated artifacts from changing the agreed product.

### II. Incremental Vertical Capabilities

Development MUST follow all eight roadmap phases in their documented order and scope, with
gates G1–G8. Each phase MUST deliver a complete, observable, executable and verifiable vertical
capability before the next begins. This constitution governs the whole project and MUST NOT
be interpreted as a Phase 1 specification.

A phase MUST depend only on infrastructure introduced in the current or previous phases,
previously implemented behavior, established contracts and new components strictly necessary
for its current capability. Specifications, plans, tasks, tests, domain types, services, ports,
repositories, database structures, dependencies and infrastructure components MUST NOT be
introduced solely for a future phase. Future endpoints, entities, repositories, application services,
integrations, infrastructure and mocks of nonexistent capabilities MUST NOT be prerequisites.

Tests MUST exercise current or previous capabilities only. Skipped, placeholder and TODO
tests MUST NOT establish completion. Later phases MAY extend earlier behavior as required
by the roadmap, but MUST preserve established contracts unless the SRS is explicitly amended.
Phase 8 MUST close operations, integration and acceptance; missing earlier functionality
MUST be corrected in its responsible phase rather than reclassified as Phase 8 scope.

Security, persistence, deployment and operational requirements MUST be introduced and
verified in their assigned phases. Their inclusion here MUST NOT authorize premature work.
Roadmap hardening or final verification MUST NOT weaken an SRS requirement on a capability
when that capability is introduced.

Rationale: each increment must work without a future phase repairing or completing it.

### III. Hexagonal Boundaries with Feature Vertical Slices

Hexagonal Architecture and Vertical Slices by feature MUST be used together. Hexagonal
boundaries MUST determine dependency direction; slices MUST organize behavior by functional
capability. The baseline solution structure MUST be:

```text
src/
├── Authentication.Domain/
├── Authentication.Application/
├── Authentication.Infrastructure/
└── Authentication.Api/
tests/
├── Authentication.UnitTests/
└── Authentication.IntegrationTests/
```

Domain MUST remain independent of every other product project. Application MAY depend on
Domain. Infrastructure MAY depend on Application and Domain. Api MUST be the composition
root and MAY depend on Application and Infrastructure. Application MUST NOT depend on
Infrastructure implementations.

Domain MUST be technology agnostic and MUST NOT depend on ASP.NET Core, EF Core, Identity,
SQLite, SMTP, HTTP concepts or infrastructure implementations. Concrete Identity types MUST
belong to Infrastructure. An artificial domain model around Identity MUST NOT be created;
domain types MUST express real domain behavior required by an implemented capability.

Each slice MUST contain only types required by its capability. Infrastructure MUST implement
Application ports only for actual use cases and boundaries. Generic repositories, dedicated
CQRS infrastructure and an internal message bus MUST NOT be introduced. Horizontal
service/repository/manager/helper/DTO hierarchies MUST NOT scatter a feature without providing
demonstrable isolation. Endpoint registration MUST reside with the feature. `Program.cs`
MUST remain primarily configuration, DI registration, middleware, endpoint mounting and
startup; business logic MUST NOT be implemented directly there.

Rationale: enforce technology boundaries while keeping each capability small and cohesive.

### IV. Deliberate Simplicity and Dependency Control

KISS and YAGNI MUST govern design. SOLID MUST guide responsibilities and dependency boundaries,
not maximize interfaces, layers, patterns or types. Among approaches satisfying the same
requirements, implementation MUST prefer fewer types, fewer dependencies, less configuration,
less infrastructure and lower conceptual complexity. Patterns and abstractions MUST solve
an existing problem with a documented current consumer or use case.

Speculative interfaces, adapters, repositories, factories, managers, caching, tenant models
and infrastructure MUST NOT be created. Additional test projects MUST have a demonstrated
need that cannot reasonably be met in the two baseline test projects. Multiple DbContexts
MUST NOT split the small database without demonstrated necessity.

Each new third-party dependency MUST satisfy an immediate active-phase requirement and be
justified in its `plan.md`. Authorization in the technical baseline MUST NOT justify installing
a dependency before its phase. SMTP, its application port and MailKit MUST first appear when
password recovery needs email delivery. NSubstitute MUST NOT be installed by default; a
specific interaction test MUST justify it. New dependencies MUST follow baseline authorization
rules; replacements for established technologies MUST first amend the technical baseline.

Without an explicit amendment of the applicable normative baseline, the project MUST NOT use
MediatR, AutoMapper, FluentValidation, FluentAssertions, Serilog, NLog, Swashbuckle, NSwag,
EF Core InMemory as a persistence-test substitute, generic repository frameworks, external
DI containers, Redis, message brokers, background job schedulers, Vault/KMS infrastructure,
OpenIddict or IdentityServer. External observability platforms, mandatory log collectors and
additional backup services MUST NOT be introduced under the current baseline.

Rationale: dependency and abstraction costs must be justified by the capability being built.

### V. Security by Construction within the Agreed Scope

ASP.NET Core Identity MUST implement the identity responsibilities assigned by the baseline.
Custom password hashing, password verification, Identity lockout, password-reset token
generation, role storage and security-stamp behavior already provided by Identity MUST NOT
be implemented. Security-sensitive randomness MUST use
`System.Security.Cryptography.RandomNumberGenerator`.

Access tokens MUST follow the SRS JWT/RS256 model with one active RSA key pair. Microsoft
IdentityModel libraries MUST perform signing and validation; custom cryptographic parsing,
signing or validation MUST NOT be implemented. Authentication API MUST be the only service
with the private signing key. Consumer APIs MUST receive only public verification material
and validate tokens locally; ordinary business requests MUST NOT call Authentication API
for validation. Manual configured key replacement MUST remain possible as defined by the SRS.

Distributed JWT blacklists, automatic signing-key rotation, mandatory JWKS, external identity
servers, Redis, Vault, KMS and HSM MUST NOT be introduced under this baseline. Issued access
tokens MUST retain the expiry-based residual-validity model defined by the SRS. Other SRS
scope exclusions MUST NOT be introduced through implementation convenience.

Passwords, access tokens, refresh tokens, reset tokens, private RSA keys, SMTP credentials
and other secrets MUST NOT be logged. Secrets MUST NOT be embedded in source, versioned
configuration, Dockerfiles or images. The deliberately prescribed initial administrator
credential is an explicit SRS bootstrap requirement, not permission to embed operational
secrets; its first-access handling and non-restoration MUST follow the SRS unchanged.

Environment-dependent configuration and operational secrets MUST come from external
configuration using native .NET facilities. Invalid required configuration MUST fail fast.
Production errors MUST NOT reveal stack traces or EF Core, SQLite or Identity internals.
Security and origin controls MUST preserve SRS requirements and the division of responsibility
between the trusted reverse proxy and the application.

Rationale: use the established security mechanisms without expanding into an IAM platform.

### VI. Tests of Implemented Behavior and Verifiable Completion

Every behavior that can reasonably be automated MUST have tests appropriate to its risk and
abstraction level. Unit tests SHOULD cover domain invariants, application use cases,
deterministic rules, state transitions and time-dependent logic; framework and storage
behavior MUST be verified through integration rather than simulated unit substitutes.

Integration tests MUST cover Identity, EF Core, SQLite, migrations, Minimal API contracts,
JWT signing/validation, authorization, ProblemDetails, Data Protection and other
infrastructure-dependent functionality when introduced. Persistence tests MUST use real
SQLite behavior. SQLite in-memory with an open connection MAY support fast SQL tests;
migration, restart and file-persistence tests MUST use temporary SQLite files. EF Core
InMemory MUST NOT stand in for SQLite constraints, transactions, migrations or queries.

Tests MUST use xUnit.net v3, built-in assertions and Microsoft Testing Platform.
HTTP integration tests MUST use `Microsoft.AspNetCore.Mvc.Testing` 10.x and
`WebApplicationFactory`. `dotnet test` MUST run the suite under .NET 10. Real objects,
simple fakes and explicit stubs MUST be preferred to mocking frameworks. Time-dependent
tests MUST use a controlled `TimeProvider` or `FakeTimeProvider`, not real sleeps. Normal
test execution MUST NOT require external SMTP; application email tests MUST use a fake,
and configured real SMTP tests MUST remain integration tests.

Deployment/acceptance tests MUST demonstrate relevant startup, persistence, restart/rebuild,
Compose teardown, routing, backup/restore and API A/B integration in their responsible roadmap
phases. Destructive teardown and restore demonstrations MUST use disposable acceptance
environments. Arbitrary coverage percentages or invented performance targets MUST NOT
replace critical behavior tests and the documented gates.

Rationale: tests provide evidence of current capability without future dependencies.

### VII. Persistence Ownership and Deployment Integrity

Authentication API MUST exclusively own its single SQLite database and schema lifecycle.
Business APIs MUST NOT access it directly. EF Core migrations MUST be version controlled,
included in the deployed application and applied automatically at startup before normal
traffic. Initialization MUST be idempotent, MUST preserve existing data and administrator
changes, and MUST NOT report readiness after migration failure.

Compose MUST NOT run `dotnet ef database update`. Migration/bootstrap containers, external
database bootstrap scripts, mandatory manual database initialization and additional database
servers MUST NOT be required. Normal deployment MUST require only supplied configuration,
secrets and persistent storage plus Docker Compose startup, as defined by the SRS.

Production SQLite, Data Protection keys, the RSA private key and persistent logs MUST remain
independent of Compose-managed volume lifecycles and survive `docker compose down -v`.
Host bind mounts MUST be the reference mechanism; explicitly externally managed Docker
volumes MAY be used where the technical baseline permits. Locations MUST be configurable
and documented. Data Protection persistence MUST preserve still-valid dependent tokens
across restart/rebuild. Persistent files MUST have restricted permissions: RSA private
material readable only by the authorized Auth API process/user; SQLite and Data Protection
writable only by Auth API and authorized operators; logs protected against unauthorized access.
Private-key mounts MUST be read-only where supported.

The final deployment MUST retain exactly four functional services: `frontend`, `auth-api`,
`api-a` and `api-b`. Frontend MUST serve Angular and provide the Nginx reverse proxy;
an extra gateway MUST NOT be added without a baseline amendment. Production browser traffic
MUST enter through that proxy with HTTPS terminated there; backend ports MUST NOT require
direct external exposure. Forwarded headers MUST be trusted only from authorized proxies or
networks. Proxy protections MUST NOT substitute for application security controls.

SQLite backup MUST produce a consistent copy through supported SQLite backup operations or
controlled suspension of writes. Restore MUST be documented and demonstrated before
production acceptance, without adding a permanent service. Persistence and final topology
MUST be built incrementally according to the roadmap, not provisioned in advance.

Rationale: deployment teardown must not destroy identities or cryptographic state.

## Technical and Repository Constraints

These decisions MUST remain in force across all phases; their applicability MUST NOT
accelerate introduction beyond the active roadmap capability:

- Backend MUST target only .NET 10 / `net10.0`, stable C# 14 and ASP.NET Core 10 Minimal APIs.
  Preview language/framework features and primary Controller-based HTTP programming MUST NOT
  be used. `global.json` MUST pin a tested .NET 10 SDK, prevent accidental major upgrades and
  support deliberate patch/feature-band updates within .NET 10.
- Identity and EF Core MUST use compatible 10.x versions. SQLite MUST use the official
  `Microsoft.EntityFrameworkCore.Sqlite` provider and baseline minimum 3.46.1.
  `Microsoft.EntityFrameworkCore.Design` MUST remain a development dependency, not a runtime
  image requirement.
- Application time abstraction MUST use `System.TimeProvider`; a custom `IClock` MUST NOT be
  introduced without a demonstrated limitation. Persisted instants and authentication timestamps
  MUST be UTC. JWT clock tolerance MUST be explicit and consistent per the SRS.
- OpenAPI MUST use `Microsoft.AspNetCore.OpenApi`, version 3.1 unless a required consumer has a
  demonstrated incompatibility. Documentation MUST use `Scalar.AspNetCore` in read-only mode:
  API client and Test Request hidden/disabled, no credential persistence or request execution.
  Development documentation MUST be enabled; production exposure MUST be disabled externally
  or restricted by Nginx to authorized networks. Endpoints MUST document contracts and errors.
- HTTP errors MUST use ProblemDetails and be translated at the HTTP boundary. Domain MUST NOT
  know HTTP codes or ProblemDetails. Small DTO transformations MUST use explicit/manual mapping.
  Validation MUST live with the owning rule and MUST NOT unnecessarily duplicate it across
  layers. Exceptions MUST NOT serve as normal control flow.
- DI MUST use the native ASP.NET Core container. Application logging MUST use
  `Microsoft.Extensions.Logging` / `ILogger<T>`, the official console provider and the baseline
  minimal custom `ILoggerProvider` for persistent files. File logging MUST implement the
  technical baseline's queued background, thread-safe UTF-8 event writing, UTC daily rotation
  and configurable retention with a 30-day default. Native `Activity` and available trace/span/
  correlation identifiers MUST be used instead of a custom tracing framework.
- SMTP MUST use MailKit as an Infrastructure adapter behind the minimal Application email
  port, introduced in the password-recovery phase. Domain and Application MUST NOT depend on
  MailKit. This MUST NOT authorize a scheduler or speculative job infrastructure.
- Microsoft C#/.NET conventions and `.editorconfig` MUST govern code style. `Nullable=enable`,
  `ImplicitUsings=enable` and `EnforceCodeStyleInBuild=true` MUST be configured. First-party
  compiler/analyzer warnings MUST be corrected. Global suppressions MUST have documented
  justification; null-forgiving operators MUST require demonstrable invariants. Standard SDK
  analyzers MUST be used; additional analyzer packages MUST require explicit justification.
- Files MUST use file-scoped namespaces and one top-level type whose name matches the file.
  Only `Program.cs`, generated code and private/nested implementation encapsulation MAY use
  the exceptions in Technical Constraints §25.1. Unrelated types and multiple public records
  MUST NOT share a file. Namespaces SHOULD reflect project and feature ownership; naming
  and async conventions MUST follow Technical Constraints §25.2.
- NuGet versions MUST be centralized in `Directory.Packages.props`, pinned to tested concrete
  versions and compatible across the Microsoft 10.x stack. Floating versions MUST NOT be used.
- Auth API container builds MUST use official Microsoft .NET 10 SDK/runtime images and
  multi-stage builds. Runtime MUST run as non-root, contain no SDK, `dotnet-ef`, unnecessary
  source or secrets, and have compatible persistent-mount permissions. Official Nginx MUST
  serve the frontend/proxy. Production image versions or digests MUST be validated and recorded;
  uncontrolled floating release tags MUST NOT be used. Alpine/chiseled Auth API images MUST NOT
  be imposed as an initial requirement.

## Development Workflow and Quality Gates

All `/speckit.specify`, `/speckit.clarify`, `/speckit.plan`, `/speckit.tasks`,
`/speckit.analyze` and `/speckit.implement` workflows MUST read and obey this constitution
and the applicable normative baseline. Generic template examples/defaults MUST yield to these
rules. Template workflow subdivisions MUST NOT change roadmap phases or their sequence.

- **Specify and clarify:** MUST identify the active roadmap phase/gate, preserve its scope,
  trace requirements to the SRS and resolve genuine questions without inventing requirements.
  Before adopting SRS requirements, MUST check identifiers, preconditions, exceptions, affected
  interfaces, consistency and viable T/D/I/A verification methods per SRS §49.4.
- **Plan:** MUST explicitly check the constitution, SRS, technical constraints, roadmap and
  active specification before design and recheck after design. MUST justify every dependency,
  abstraction and infrastructure component by current need, preserving the stack and dependency
  direction. Complexity documentation MUST NOT authorize a baseline violation.
- **Tasks:** MUST derive necessary work from the active specification and approved plan,
  identify executable dependencies within the active phase and include required tests and
  gate verification. Tasks MUST be executable and testable without future capabilities.
  Generic “tests optional” guidance MUST NOT omit tests required by this constitution.
- **Analyze:** MUST identify product, architecture, technology and dependency-direction
  violations, phase leakage, speculative abstractions, unjustified dependencies, premature
  infrastructure, missing tests, future-dependent tests, broken contracts and missing or
  unverifiable acceptance criteria. These defects MUST be resolved before implementation.
- **Implement:** MUST follow the approved specification, plan, tasks and applicable phase gate.
  Implementation MUST NOT begin with unresolved critical contradictions against the baseline.
  Baseline changes MUST NOT be inferred from implementation convenience.

A task MUST compile, satisfy its traced requirement, have automated tests where feasible,
preserve regression tests, expose no secrets and add no unused future scope. A feature MUST
NOT be declared done with architectural violations, unauthorized dependencies, speculative
components, unresolved first-party warnings, missing critical tests, broken tests/contracts,
exposed secrets, undocumented manual deployment steps, phase leakage or a failing roadmap gate.

A phase MUST have completed tasks, operational current functionality, passing current and
previous tests, an observable vertical demonstration, updated relevant documentation and
satisfied persistence requirements where applicable. All of the following MUST pass:

```text
build        PASS
tests        PASS
startup      PASS
feature      PASS
regression   PASS
```

The roadmap gate MUST be approved, its checklist and decision records updated and an
identifiable closing commit created per RD-007; final closure MUST follow G8's commit/tag
requirement. Completion MUST be recorded using roadmap states and progress records, and MUST
NOT be inferred from task count alone. Updates to baseline-hosted tracking records MUST be
explicit, reviewed and version controlled; they MUST NOT silently amend requirements or
sequencing. Failing tests or incomplete behavior MUST NOT be deferred to a future phase.

## Governance

This constitution governs compliance with the normative baseline. Reviews and Spec Kit
checks MUST enforce it under Principle I's precedence. Generated artifacts, template examples,
complexity exceptions and constitution amendments MUST NOT authorize violating the baseline.

Amendments MUST be deliberate, documented, reviewed and version controlled. A proposed
baseline change MUST identify the affected document and requirement/constraint, proposed
change, reason and impact on specifications, plans, tasks, tests and implementation.
Product behavior/scope changes MUST first amend the SRS; architecture, technology,
dependencies and implementation constraints MUST first amend Technical Constraints;
phase sequencing, dependencies and gates MUST first amend the Roadmap. Affected lower
sources and this constitution MUST then be reconciled with that approved baseline change.
Unresolved material ambiguities MUST be recorded for explicit resolution rather than
silently converted into engineering principles.

Each constitution amendment MUST record its rationale and impact on templates, specifications,
plans, tasks and implementation guidance. Necessary synchronization MUST be identified and
completed through appropriately scoped changes; this constitution-generation operation MUST
NOT rewrite template sources or application/feature artifacts. The temporary Sync Impact
Report MUST be reviewed and removed before committing the constitution.

Versions MUST follow semantic versioning: MAJOR for incompatible principle removal or
redefinition; MINOR for new principles/sections or materially expanded guidance; PATCH for
non-semantic clarifications and corrections. Initial adoption is `1.0.0`. Amendments MUST
retain the original ratification date, update the last-amended date and keep version metadata
consistent with the documented change.

**Version**: 1.0.0 | **Ratified**: 2026-10-07 | **Last Amended**: 2026-10-07
