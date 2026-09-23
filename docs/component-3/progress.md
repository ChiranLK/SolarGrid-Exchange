# Component 3: Energy Reservation Workflow Progress

Audit date: 2026-09-23

Branch inspected: `feature/reservation-workflow` at `970f578`

Scope of this update: repository inspection, branch verification, and documentation only

Latest implementation update: 2026-09-23 at branch `HEAD` `89fe8b5` plus uncommitted Component 3 service-foundation changes

## Component 3 requirements

### Confirmed requirements

The task brief establishes the following requirements:

- Deliver the complete Energy Reservation Workflow as Component 3.
- Keep reservation business rules in the central C# Web API.
- Have the web and native Android clients use the workflow only through REST API calls.
- Preserve the repository's architecture and naming and reuse existing models, services, and frameworks.
- Integrate with Member 1's authentication/user/prosumer/session work, Member 2's station/slot/schedule/capacity/map work, and Member 4's dashboard/history/search/QR/completion/deployment work.
- Do not expose secrets or automatically commit, push, merge, or deploy.

The API configuration also contains two apparently reservation-oriented values: `MaxBookingDaysAhead = 7` and `MinChangeNoticeHours = 12`. They are not currently registered through options or used by any service. The missing assignment/team-plan artifact must confirm whether these values and their exact boundary behavior are acceptance requirements.

### Provisional delivery scope requiring assignment confirmation

No assignment specification or team plan is present in the checkout or in the available local branch history. Based on the existing names and integration points, the likely workflow still needs:

- Reservation entity, status lifecycle, request/response/query DTOs, service, and authenticated controller endpoints.
- Prosumer-owned create, retrieve/list, update/reschedule, and cancel operations, with any staff read/management operations required by the assignment.
- Server-side validation for reservation ownership, user/account eligibility, active stations, available/future slots, requested energy, booking horizon, change/cancellation notice, and allowed status transitions.
- Atomic capacity reservation/release against `EnergyBookingSlots`, including concurrency protection and consistent `Available`/`FullyBooked` slot status updates.
- Contracts needed by Member 4 for reservation history/search, QR verification, and completion, without moving those business rules into either client.
- REST integrations and user interfaces in the web and Android applications once those projects are supplied.
- Automated unit/integration tests and manual API/client verification evidence.

The assignment document is required before treating this provisional list as the final acceptance contract, particularly for status names, exact endpoints, permissions, QR payload/verification rules, completion behavior, and edit/cancellation cutoffs.

## Existing implementation

### Repository and framework baseline

- The only solution project is `SolarMicrogrid.API/SolarMicrogrid.API.csproj`.
- Backend: ASP.NET Core Web API using controller classes and the .NET minimal hosting model, targeting `net10.0` with C# nullable reference types and implicit usings enabled.
- Data access: MongoDB.Driver `3.12.0`; there is no repository abstraction or ORM layer.
- Authentication: ASP.NET Core JWT bearer authentication with HMAC-SHA256 tokens. BCrypt (`BCrypt.Net-Next` `4.2.0`, work factor 12) hashes passwords.
- OpenAPI: `Microsoft.AspNetCore.OpenApi` is registered and mapped in Development.
- Web application: no web project, source, package manifest, or REST client is tracked in any available local or remote-tracking ref. A web framework therefore cannot be identified.
- Native Android application: no Gradle project, Android manifest, Java/Kotlin files, XML layouts, or Compose source is tracked in any available ref. Android language and XML-versus-Compose usage therefore cannot be identified.
- Tests: no test project, test source, or test runner configuration is tracked. `SolarMicrogrid.API.http` is a manual request file, not an automated test suite.
- Deployment: no container, CI/CD, or hosting configuration is tracked.

### Authentication, claims, and roles

- `UserRole` values are `Backoffice`, `GridOperator`, and `Prosumer`.
- `UserStatus` values are `PendingActivation`, `Active`, and `Deactivated`.
- Self-registration creates only a `Prosumer` in `PendingActivation`; login rejects pending and deactivated accounts.
- JWTs contain `ClaimTypes.NameIdentifier` (NIC), `ClaimTypes.Email`, `ClaimTypes.Name` (full name), and `ClaimTypes.Role` (the enum name).
- JWT validation checks issuer, audience, signing key, and lifetime. Token lifetime is configured as 60 minutes.
- `GET /api/auth/me` demonstrates reading NIC, email, and role from claims.
- The tracked base settings contain a non-empty JWT key value. This document intentionally does not reproduce it. Member 1 should confirm that it is a disposable placeholder or rotate/remove it from tracked configuration and use user-secrets/environment configuration.

### API response and error conventions

- Controllers return DTOs directly with `Ok`, `CreatedAtAction`, or an empty `NotFound` response; there is no success-response envelope.
- Paginated station/slot responses on `origin/develop` use `Items`, `TotalCount`, `Page`, `PageSize`, and `TotalPages` (serialized using the ASP.NET web JSON convention).
- Shared exception middleware on `origin/develop` maps argument errors to 400, unauthorized to 401, forbidden to 403, missing resources to 404, conflicts to 409, and unexpected errors to 500. Its explicit error body is `{ "status": number, "message": string }`.
- `[ApiController]` data-annotation/model-state failures use ASP.NET Core's standard validation problem response rather than the custom error body.
- The current feature branch contains the middleware class but does not activate it; activation is among the newer `origin/develop` commits.

### MongoDB model and collection conventions

Configured collection names are:

- `Users`: `User` documents keyed by NIC strings; a case-insensitive unique email index is created.
- `SolarStationInfo`: station documents keyed by MongoDB ObjectId strings; indexes cover GeoJSON location, active state, and name.
- `EnergyBookingSlots`: slot documents keyed by ObjectId strings and referring to station ObjectIds; indexes cover station, UTC start, availability status, station/start, and a unique station/start/end tuple.
- `EnergyReservations`: configured by name, but not exposed by `MongoDbContext`; the two context lines are commented out and no reservation indexes exist.

Stations use a GeoJSON point and decimal capacities. Slots expose total and available kWh and the statuses `Available`, `FullyBooked`, and `Unavailable`.

### Date and time conventions

- Persisted timestamps use `DateTime` properties suffixed `Utc`, `[BsonDateTimeOptions(Kind = DateTimeKind.Utc)]`, and `DateTime.UtcNow`.
- Member 2's latest slot service in `origin/develop` rejects an unspecified/default timestamp, requires an explicit offset, and converts input to UTC.
- Station operating times are local wall-clock strings in strict `HH:mm` form. Slot validation interprets them in the `Asia/Colombo` time zone and requires a slot to fit within one local operating day.
- The reservation scaffold has no date fields or normalization behavior yet.

### SQLite implementation

No SQLite database, Room dependency, Android database helper/DAO, schema, migration, cached-session model, or related test exists in any available ref. Member 1's Android session/SQLite dependency cannot be integrated or verified from this checkout.

### Existing reservation-related files

| File/integration point | Current state |
| --- | --- |
| `Models/Entities/EnergyReservation.cs` | Implemented reservation/status/history entities with ObjectId references, NIC ownership, UTC schedule/audit timestamps, Decimal128 kWh quantity, staff/action audit fields, versioning, and hashed creation-key/fingerprint identifiers. |
| `Services/ReservationService.cs` | Foundation plus creation workflow implemented: claims-to-current-user validation, self/staff permissions, target eligibility, station/slot/schedule/horizon/quantity checks, overlap protection, Pending creation, idempotent capacity hold/resume, allowed-action projection, and transaction/compensation orchestration. Update/cancel/approve/reject remain pending. |
| `Services/ReservationCapacityService.cs` | Added bounded compare-and-swap holds, same-slot adjustments, exact-claim releases, idempotent retries, and claim discovery for repair. |
| `Services/MongoTransactionRunner.cs` | Added transaction-preferred execution with fallback only for MongoDB's definitive unsupported-transaction response. |
| `Controllers/ReservationsController.cs` | Implements authenticated `POST /api/reservations` and `POST /api/reservations/staff`; reads only actor claims/header/body and delegates all rules to the service. |
| Reservation DTOs | Added separate create, staff-create, update, cancel, approve, reject, query, list, detail, status-history, paged-response, and server-derived allowed-action contracts under `Models/DTOs/Reservations`. |
| `Data/MongoDbContext.cs` | Exposes typed `EnergyReservations` and Component 3-owned `ReservationSchedulingGuards` collections. |
| `Data/MongoDbIndexInitializer.cs` | Adds reservation query/duplicate/idempotency indexes, allocation-claim lookup, and scheduling-lease TTL cleanup. |
| `Models/Entities/EnergyBookingSlot.cs` | Preserves Member 2 fields and adds a minimal embedded allocation ledger plus lookup index so reservation releases are exact and repeat-safe. |
| `Settings/MongoSettings.cs` and `appsettings.json` | Define the reservation and scheduling-guard collection names. |
| `Settings/BusinessRulesSettings.cs` | Defines the seven-day booking horizon and twelve-hour change notice; options are now bound and startup-validated, and creation consumes the horizon setting. |
| `Program.cs` | Registers transaction, capacity, scheduling-guard, and reservation services; binds business rules and activates shared exception middleware. |
| `Services/StationService.cs` | Deliberately blocks station deactivation until Component 3 supplies queryable active-reservation statuses/fields. |
| API request examples | `SolarMicrogrid.API.http` contains sanitized Prosumer self-create and staff on-behalf-of examples with JWT and idempotency headers. |
| Tests and clients | No reservation tests, web integration, Android integration, or SQLite synchronization code exists. |

The original zero-byte reservation files were introduced as scaffolds in commit `14c0fc4`. The domain/persistence foundation and reservation creation API are now implemented; the remaining lifecycle mutations still use their DTO scaffolds only.

The explicit creation task superseded the earlier self-create-only contract: Backoffice may create for any eligible active Prosumer, while Grid Operator creation is restricted to the actor's stored assigned station. Staff creation does not grant either role update/cancel rights and does not grant Grid Operators approval rights.

### Team ownership evidence and boundaries

Ownership below comes from the task brief; commit/file evidence describes what is actually present.

#### Member 1: authentication, users, prosumers, Android session/SQLite

- Implemented backend evidence: `User` entity/role/status enums, auth DTOs, BCrypt helper, JWT helper, `AuthService`, `AuthController`, Mongo user access, and unique email index. Commits are primarily authored by `sankamaduwantha`/Sanka, with Mongo foundation contributions by Chiran.
- Incomplete/absent: `UserService.cs`, `ProsumerService.cs`, `UsersController.cs`, and `ProsumersController.cs` are zero-byte scaffolds. Staff creation/activation and prosumer management mentioned in comments are not implemented. Android session and SQLite code are absent.
- Component 3 dependency: authoritative NIC claim, role/status rules, active-prosumer checks, and the eventual Android token/session interface.

#### Member 2: stations, slots, schedules, capacity, maps

- Current feature branch: station/slot entities and DTO contracts, Mongo collections/indexes, and `StationService` are present. Station/slot controllers and `SlotService` are still zero-byte scaffolds.
- Latest available `origin/develop`: adds complete station/slot controllers, slot service, pagination DTOs, DI registration, shared error middleware activation, and manual station/slot API examples. It includes authenticated station/slot reads, Backoffice-only writes, nearby-station GeoJSON queries, operating-schedule validation, overlap prevention, capacity preservation during slot edits, and optimistic update filters.
- No map client/UI is present; only the geospatial backend endpoint exists.
- Component 3 dependency: integrate the latest slot API/data contract before implementing reservation capacity changes. Coordinate atomic reservation/release semantics so Component 3 does not duplicate or bypass Member 2's slot rules.

#### Member 4: dashboard, history/search, QR verification, completion, deployment

- `DashboardController.cs` is a zero-byte scaffold.
- A fresh remote fetch found no reservation history/search endpoint, QR model/payload/verification code, completion workflow, deployment configuration, or related tests in Member 4's available branch or other team refs.
- The only commits associated with the apparent Member 4 branch/author (`Havindu`/`YourName`) update `.gitignore`; ownership identity should be confirmed in the missing team plan.
- Component 3 dependency: agree on reservation IDs/statuses, search fields, QR payload/expiry/anti-replay contract, completion transition, and dashboard projection needs before freezing the reservation schema.

## Missing implementation

1. Obtain and record the official Component 3 use cases, field definitions, status machine, role matrix, endpoints, UI requirements, and marking rubric.
2. Bring this feature branch up to date with the agreed `develop` baseline using a non-destructive team-approved integration step; do not reimplement Member 2's newer work.
3. Implement update/cancel/approve/reject workflows with version-filtered writes and the same mutation-idempotency guarantees.
4. Implement authenticated list/detail and remaining mutation endpoints.
5. Integrate `HasActiveReservationsForStationAsync` into Member 2's station-deactivation transaction/check after the branch is synchronized.
6. Supply history/search and QR/completion integrations to Member 4 without taking ownership of their UI/dashboard/deployment work.
7. Add web/Android clients plus topology-specific integration, concurrency, idempotency, and failure-injection tests.

## Dependencies

- **Missing source artifacts:** official assignment/team-plan documents, web project, Android project, and Member 1's Android session/SQLite implementation.
- **Branch baseline:** committed `HEAD` `76aebc0` matches `origin/feature/reservation-workflow`. It is 3 commits ahead and 16 commits behind local `origin/develop`; the creation changes described here are currently uncommitted. No merge/rebase was performed.
- **Member 1 contract:** authenticated active-user lookup, role/status enforcement, prosumer endpoints, and mobile session/token persistence.
- **Member 2 contract:** the shared slot entity now has a minimal allocation ledger and atomic capacity service; Member 2 must integrate active-reservation schedule/deactivation checks after branch synchronization.
- **Member 4 contract:** no completion implementation exists in available refs; reservation history/search representation, QR issuance/verification boundary, completion transition, and deployment expectations remain dependencies.
- **Runtime/configuration:** .NET SDK 10.0.401 is installed. Usable MongoDB/JWT development configuration is still needed for runtime verification. Secret values must remain outside committed configuration.
- **Schema decisions:** lifecycle/status, UTC, and staff creation permissions are documented and mapped. Energy decimal precision/rounding, completion accounting, and Member 4 QR details still require confirmation.

## Verification status

| Check | Actual result |
| --- | --- |
| Repository instructions | No `AGENTS.md` or other repository instruction file found. |
| README/assignment/team plan | Read the only README. No assignment or team-plan artifact exists in the checkout, any available ref tree, or tracked document history. |
| Git branch | PASS: active branch is `feature/reservation-workflow`, tracking `origin/feature/reservation-workflow`, with `+0/-0` against that upstream before this document was added. No branch creation was needed. |
| Safe branch preparation | PASS: no reset, checkout, merge, rebase, commit, push, or file discard performed. |
| Git baseline comparison | INFO: current `HEAD` is 16 commits behind local `origin/develop` and has no commits outside it. |
| Backend/framework inspection | PASS: ASP.NET Core controller API targeting `net10.0` confirmed from project/source. |
| Web inspection | BLOCKED/ABSENT: no web application is tracked, so its framework and API integration cannot be verified. |
| Android inspection | BLOCKED/ABSENT: no Android application is tracked, so Java/Kotlin, XML/Compose, REST integration, session, and SQLite cannot be verified. |
| MongoDB inspection | PASS (static): reservation collection mapping, BSON attributes, references, UTC fields, Decimal128 energy quantity, versioning, query/duplicate indexes, allocation claims, and per-Prosumer scheduling leases are implemented. No live database connection was attempted. |
| Reservation domain/DTO implementation | PASS (compile verified): entity/status/history/capacity state, request/query/response DTOs, collection mapping, and indexes are implemented. |
| Reservation creation API | PASS (compile verified): Prosumer self-create and Backoffice/assigned-Grid-Operator staff create validate current identities, eligibility, references, schedule, inclusive seven-day horizon, quantity, duplicate/overlap, atomic availability, Pending state, exact-once hold, hashed idempotency, and actor-derived allowed actions. |
| Automated tests | BLOCKED/ABSENT: no test project exists. |
| Current-branch backend build | BLOCKED BY PRE-EXISTING BASELINE: compilation reaches source analysis but fails because this branch lacks Member 2's `PagedStationResponseDto` and `NearbyStationResponseDto`; both already exist on `origin/develop`. No Component 3 error was reported before those failures. |
| Component 3 integration build | PASS: overlaid all Component 3 changes onto a temporary `origin/develop` tree and built with .NET SDK 10.0.401: 0 compilation errors. NuGet emitted one vulnerability-data warning because the sandbox could not reach `api.nuget.org`. Member 2's latest `SlotService` compiles with the allocation-ledger extension. No merge or tracked Member 2 implementation copy was performed, and the temporary tree was removed. |
| Assignment comment condition | PASS (static): every new/modified C# file has the required header block and every added/modified method begins with an explanatory inline comment. |
| Manual API/runtime verification | NOT RUN: no live MongoDB topology, configured users/stations/slots, or safe JWT development secret was available. Transaction and standalone compensation paths still require integration/failure-injection tests. |
| Secret review | ATTENTION: a non-empty JWT key is present in tracked base settings; value not reproduced. Ownership/rotation/removal needs confirmation. |

## Assignment evidence needed

### Inputs required from the team/course

- Original assignment brief, marking rubric, Component 3 acceptance criteria, and current team plan/ownership matrix.
- Web and Android repositories/projects if they are maintained separately.
- Member 1's session/SQLite contract and Member 4's QR/completion/history contracts.
- Agreed test accounts/roles and a sanitized development configuration procedure.

### Evidence to produce during implementation

- Traceability table from each confirmed Component 3 requirement to API endpoint, service rule, client screen/action, and automated test.
- API evidence for authorized success and validation/401/403/404/409 cases, with tokens and secrets redacted.
- MongoDB evidence showing reservation documents and before/after slot capacity without exposing credentials or personal data.
- Concurrency evidence that competing requests cannot overbook and that failed/cancelled/rescheduled operations leave capacity consistent.
- Web and Android evidence showing all calls go through the REST API; Android offline/session behavior should include SQLite schema/migration and reconnect behavior evidence if required.
- End-to-end evidence for create, view, edit/reschedule, cancel, history/search, QR verification, and completion according to the final assignment scope.
- Automated test results, build output, and a clean `git diff`/`git status` review before the user chooses to commit.

Suggested commit message if this documentation is later committed by the user:

`docs(reservations): document component 3 scope and dependencies`
