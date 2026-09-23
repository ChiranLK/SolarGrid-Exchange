# Component 3: Energy Reservation Workflow Progress

Audit date: 2026-09-23

Branch inspected: `feature/reservation-workflow` at `970f578`

Scope of this update: repository inspection, branch verification, and documentation only

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
| `Models/Entities/EnergyReservation.cs` | Zero-byte scaffold; no entity, fields, statuses, or BSON mapping. |
| `Services/ReservationService.cs` | Zero-byte scaffold; no business logic. |
| `Controllers/ReservationsController.cs` | Zero-byte scaffold; no REST endpoints or authorization. |
| Reservation DTOs | No reservation DTO directory or DTO files exist. |
| `Data/MongoDbContext.cs` | Reservation collection construction/property are present only as commented lines. |
| `Data/MongoDbIndexInitializer.cs` | No reservation indexes. |
| `Settings/MongoSettings.cs` and `appsettings.json` | Define `EnergyReservations` as the intended collection name. |
| `Settings/BusinessRulesSettings.cs` | Defines 7-day booking horizon and 12-hour change notice defaults, but uses class name `BusinessRules`, is not registered, and is not consumed. |
| `Program.cs` | Validates the reservation collection name but does not register reservation options/service. |
| `Services/StationService.cs` | Deliberately blocks station deactivation until Component 3 supplies queryable active-reservation statuses/fields. |
| API request examples | No reservation requests exist in either the current file or `origin/develop`. |
| Tests and clients | No reservation tests, web integration, Android integration, or SQLite synchronization code exists. |

The three zero-byte reservation files were introduced as scaffolds in commit `14c0fc4`; no later available commit implements them.

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
- No reservation history/search endpoint, QR model/payload/verification code, completion workflow, deployment configuration, or related tests are present.
- The only commits associated with the apparent Member 4 branch/author (`Havindu`/`YourName`) update `.gitignore`; ownership identity should be confirmed in the missing team plan.
- Component 3 dependency: agree on reservation IDs/statuses, search fields, QR payload/expiry/anti-replay contract, completion transition, and dashboard projection needs before freezing the reservation schema.

## Missing implementation

1. Obtain and record the official Component 3 use cases, field definitions, status machine, role matrix, endpoints, UI requirements, and marking rubric.
2. Bring this feature branch up to date with the agreed `develop` baseline using a non-destructive team-approved integration step; do not reimplement Member 2's newer work.
3. Define one reservation entity/status model and DTO set following existing BSON, ObjectId, decimal, UTC, and naming conventions.
4. Enable `MongoDbContext.Reservations`, create required indexes, bind the business-rule settings, and register `ReservationService`.
5. Implement authenticated REST endpoints and central service rules for the confirmed workflow.
6. Make slot capacity allocation/release atomic and concurrency-safe. Define idempotency and rollback behavior for create, edit/reschedule, cancel, and completion.
7. Replace Member 2's station-deactivation blocker with an active-reservation query once the status contract exists.
8. Supply history/search and QR/completion contracts to Member 4 without taking ownership of their UI/dashboard/deployment work.
9. Add web and Android REST integrations only after their actual projects/frameworks are available; do not invent client frameworks.
10. Add automated service/controller/integration/concurrency tests and sanitized manual request examples.

## Dependencies

- **Missing source artifacts:** official assignment/team-plan documents, web project, Android project, and Member 1's Android session/SQLite implementation.
- **Branch baseline:** current `HEAD` is an ancestor of local `origin/develop` and is 16 commits behind it. The feature branch itself exactly matches `origin/feature/reservation-workflow` at audit time. No merge/rebase was performed.
- **Member 1 contract:** authenticated active-user lookup, role/status enforcement, prosumer endpoints, and mobile session/token persistence.
- **Member 2 contract:** latest station/slot implementation and a shared atomic capacity update strategy.
- **Member 4 contract:** reservation history/search representation, QR issuance/verification boundary, completion transition, and deployment expectations.
- **Runtime/configuration:** a .NET 10 SDK and usable MongoDB/JWT development configuration are needed for build/runtime verification. Secret values must remain outside committed configuration.
- **Schema decisions:** reservation statuses, active-status set, requested-energy units/precision, timestamps, audit fields, cancellation/completion metadata, and any QR fields must be agreed before indexes or cross-component queries are finalized.

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
| MongoDB inspection | PASS (static): configuration, collections, BSON mappings, and current indexes inspected; reservation collection remains disabled. No live database connection was attempted. |
| Reservation implementation | FAIL/NOT STARTED: entity, service, and controller are zero-byte scaffolds; DTOs, indexes, registration, endpoints, and tests are absent. |
| Automated tests | BLOCKED/ABSENT: no test project exists. |
| Build/test execution | BLOCKED: the machine has .NET 8 runtimes but reports no installed .NET SDK; the project targets .NET 10. No build or test could run. |
| Manual API verification | NOT RUN: no SDK, no reservation endpoints, and no live MongoDB verification configuration were available. |
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
