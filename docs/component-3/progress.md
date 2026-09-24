# Component 3: Energy Reservation Workflow Progress

Audit date: 2026-09-24

Branch inspected: `feature/reservation-workflow` at merged `develop` baseline `f64b97f`

Scope of this update: complete staff-on-behalf reservation creation with bounded Prosumer search, review, idempotent timeout reconciliation, and a saved summary

Latest implementation update: 2026-09-24 on `feature/reservation-workflow`

## Component 3 requirements

### Confirmed requirements

The task brief establishes the following requirements:

- Deliver the complete Energy Reservation Workflow as Component 3.
- Keep reservation business rules in the central C# Web API.
- Have the web and native Android clients use the workflow only through REST API calls.
- Preserve the repository's architecture and naming and reuse existing models, services, and frameworks.
- Integrate with Member 1's authentication/user/prosumer/session work, Member 2's station/slot/schedule/capacity/map work, and Member 4's dashboard/history/search/QR/completion/deployment work.
- Do not expose secrets or automatically commit, push, merge, or deploy.

The API configuration contains `MaxBookingDaysAhead = 7` and `MinChangeNoticeHours = 12`. Both are registered through validated options and enforced by the reservation service with inclusive boundary tests.

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

- The solution contains the API plus the xUnit `SolarMicrogrid.API.Tests` project.
- Backend: ASP.NET Core Web API using controller classes and the .NET minimal hosting model, targeting `net10.0` with C# nullable reference types and implicit usings enabled.
- Data access: MongoDB.Driver `3.12.0`; there is no repository abstraction or ORM layer.
- Authentication: ASP.NET Core JWT bearer authentication with HMAC-SHA256 tokens. BCrypt (`BCrypt.Net-Next` `4.2.0`, work factor 12) hashes passwords.
- OpenAPI: `Microsoft.AspNetCore.OpenApi` is registered and mapped in Development.
- Web application: `SolarMicrogrid.Web` provides the shared React/TypeScript application using Vite, Bootstrap 5, React Router, one API client, API-backed JWT session restoration, protected/role routes, a responsive layout, reusable async/status/confirmation components, and API-backed reservation list/detail/action screens.
- Native Android application: no Gradle project, Android manifest, Java/Kotlin files, XML layouts, or Compose source is tracked in any available ref. Android language and XML-versus-Compose usage therefore cannot be identified.
- Tests: `SolarMicrogrid.API.Tests` uses xUnit and the .NET test SDK. Its integration fixture uses a unique database, production MongoDB indexes/services, and a fixed injectable server clock. `scripts/run-component3-tests.ps1` runs a disposable MongoDB 8 replica set and removes it afterward.
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
- Shared exception middleware is active before authentication and authorization in the current merged API.

### MongoDB model and collection conventions

Configured collection names are:

- `Users`: `User` documents keyed by NIC strings; a case-insensitive unique email index is created.
- `SolarStationInfo`: station documents keyed by MongoDB ObjectId strings; indexes cover GeoJSON location, active state, and name.
- `EnergyBookingSlots`: slot documents keyed by ObjectId strings and referring to station ObjectIds; indexes cover station, UTC start, availability status, station/start, and a unique station/start/end tuple.
- `EnergyReservations`: exposed by `MongoDbContext` with ownership, station, status/time, stable ordering, idempotency, and active-duplicate indexes.

Stations use a GeoJSON point and decimal capacities. Slots expose total and available kWh and the statuses `Available`, `FullyBooked`, and `Unavailable`.

### Date and time conventions

- Persisted timestamps use `DateTime` properties suffixed `Utc`, `[BsonDateTimeOptions(Kind = DateTimeKind.Utc)]`, and `DateTime.UtcNow`.
- Member 2's latest slot service in `origin/develop` rejects an unspecified/default timestamp, requires an explicit offset, and converts input to UTC.
- Station operating times are local wall-clock strings in strict `HH:mm` form. Slot validation interprets them in the `Asia/Colombo` time zone and requires a slot to fit within one local operating day.
- Reservation and slot timestamps are stored in UTC and returned for locale-aware client formatting.

### SQLite implementation

No SQLite database, Room dependency, Android database helper/DAO, schema, migration, cached-session model, or related test exists in any available ref. Member 1's Android session/SQLite dependency cannot be integrated or verified from this checkout.

### Existing reservation-related files

| File/integration point | Current state |
| --- | --- |
| `Models/Entities/EnergyReservation.cs` | Implements reservation/status/history entities with ObjectId references, NIC ownership, UTC schedule/audit timestamps, Decimal128 kWh quantity, staff/action audit fields, versioning, and scoped creation/update/cancel/approve/reject idempotency hashes. |
| `Services/ReservationService.cs` | Implements create, update/reschedule, cancellation, approval, rejection, role-scoped paged lists, and object-authorized details. Read filters execute in MongoDB, enrich DTOs with batched station/slot display data, and return current actor-scoped actions. |
| `Services/ReservationReadPolicy.cs` | Centralizes Prosumer ownership, Grid Operator station scope, Pending/Current/ApprovedFuture/History definitions, page-count math, and allowed-action reasons used by API projections and focused tests. |
| `TimeProvider` integration | Reservation lifecycle, capacity-allocation timestamps, and scheduling-lease expiry use one injectable server clock; production registers `TimeProvider.System`, while tests use a fixed UTC provider. |
| `Services/ReservationCapacityService.cs` | Added bounded compare-and-swap holds, same-slot adjustments, exact-claim releases, idempotent retries, and claim discovery for repair. |
| `Services/MongoTransactionRunner.cs` | Added transaction-preferred execution with fallback only for MongoDB's definitive unsupported-transaction response. |
| `Controllers/ReservationsController.cs` | Implements authenticated list, object-authorized detail, create, update, cancel, approve, and reject routes; identity, role, lifecycle, capacity, and read scope remain service-owned. |
| Reservation DTOs | Added separate create, staff-create, update, cancel, approve, reject, query, list, detail, status-history, paged-response, and server-derived allowed-action contracts under `Models/DTOs/Reservations`. |
| `Data/MongoDbContext.cs` | Exposes typed `EnergyReservations` and Component 3-owned `ReservationSchedulingGuards` collections. |
| `Data/MongoDbIndexInitializer.cs` | Adds reservation ownership/station/status/time and stable newest-first query indexes, duplicate/idempotency indexes, allocation-claim lookup, and scheduling-lease TTL cleanup. |
| `Models/Entities/EnergyBookingSlot.cs` | Preserves Member 2 fields and adds a minimal embedded allocation ledger plus lookup index so reservation releases are exact and repeat-safe. |
| `Settings/MongoSettings.cs` and `appsettings.json` | Define the reservation and scheduling-guard collection names. |
| `Settings/BusinessRulesSettings.cs` | Defines the seven-day booking horizon and twelve-hour change notice; options are now bound and startup-validated, and creation consumes the horizon setting. |
| `Program.cs` | Registers user, transaction, capacity, scheduling-guard, and reservation services; binds business rules and activates shared exception middleware. |
| `UsersController.cs` and `UserService.cs` | Reuse Member 1's authorized user API and add a bounded, paged active-Prosumer search for staff reservation ownership selection. Search is executed in MongoDB and returns only NIC, name, and email. |
| `Services/StationService.cs` | Deliberately blocks station deactivation until Component 3 supplies queryable active-reservation statuses/fields. |
| API request examples | `SolarMicrogrid.API.http` contains sanitized list/search/detail and mutation examples with JWT, paging/filter, idempotency, expected-version, and reason inputs. |
| Tests and clients | Thirty-one real-MongoDB integration tests cover lifecycle, security, concurrency, eligible-Prosumer lookup, and staff-created owner visibility; eight focused xUnit tests preserve read scope, server-side predicate translation, filters, paging, history, and action-refresh coverage. The shared web reservation UI and fourteen focused web checks are present; Android and SQLite integration remain absent. |

The original zero-byte reservation files were introduced as scaffolds in commit `14c0fc4`. The domain/persistence foundation plus create, update/reschedule, cancellation, approval, and rejection APIs are now implemented.

The explicit lifecycle tasks superseded the earlier self-only/Backoffice-only draft: Backoffice may create, update, cancel, approve, and reject; Grid Operators may create, update, cancel, and approve only within their stored assigned station. Rejection remains Backoffice-only, and staff receive no notice, horizon, status, version, reference, or capacity bypass.

### Team ownership evidence and boundaries

Ownership below comes from the task brief; commit/file evidence describes what is actually present.

#### Member 1: authentication, users, prosumers, Android session/SQLite

- Implemented backend evidence: `User` entity/role/status enums, auth DTOs, BCrypt helper, JWT helper, `AuthService`, `AuthController`, Mongo user access, and unique email index. Commits are primarily authored by `sankamaduwantha`/Sanka, with Mongo foundation contributions by Chiran.
- Implemented user API evidence: Member 1's `UserService`/`UsersController` provide staff management and authorized user lists; Component 3 adds a staff-only paged eligible-Prosumer search without duplicating management in React. `ProsumerService.cs`, `ProsumersController.cs`, Android session, and SQLite code remain absent.
- Component 3 dependency: authoritative NIC claim, role/status rules, active-prosumer checks, and the eventual Android token/session interface.

#### Member 2: stations, slots, schedules, capacity, maps

- Current feature branch: complete station/slot controllers, services, pagination DTOs, DI registration, and manual API examples are present. They include authenticated station/slot reads, Backoffice writes, nearby-station GeoJSON queries, operating-schedule validation, overlap prevention, capacity preservation during slot edits, and optimistic update filters.
- No map client/UI is present; only the geospatial backend endpoint exists.
- Component 3 dependency: integrate the latest slot API/data contract before implementing reservation capacity changes. Coordinate atomic reservation/release semantics so Component 3 does not duplicate or bypass Member 2's slot rules.

#### Member 4: dashboard, history/search, QR verification, completion, deployment

- `DashboardController.cs` is a zero-byte scaffold.
- A fresh remote fetch found no reservation history/search endpoint, QR model/payload/verification code, completion workflow, deployment configuration, or related tests in Member 4's available branch or other team refs.
- The only commits associated with the apparent Member 4 branch/author (`Havindu`/`YourName`) update `.gitignore`; ownership identity should be confirmed in the missing team plan.
- Component 3 dependency: agree on reservation IDs/statuses, search fields, QR payload/expiry/anti-replay contract, completion transition, and dashboard projection needs before freezing the reservation schema.

## Missing implementation

1. Obtain and record the official Component 3 use cases, field definitions, status machine, role matrix, endpoints, UI requirements, and marking rubric.
2. Implement the remaining QR verification/completion mutation endpoints with Member 4.
3. Integrate `HasActiveReservationsForStationAsync` into Member 2's station-deactivation transaction/check.
4. Supply history/search and QR/completion integrations to Member 4 without taking ownership of their UI/dashboard/deployment work.
5. Implement remaining team-owned non-reservation web screens, add the Android client, and add standalone-Mongo compensation failure-injection tests.

## Dependencies

- **Missing source artifacts:** official assignment/team-plan documents, Android project, and Member 1's Android session/SQLite implementation.
- **Branch baseline:** `feature/reservation-workflow` was fast-forwarded to merged `develop` commit `f64b97f` before this staff-creation update; Member 1's user API commit `4132887` was integrated with authorship preserved.
- **Member 1 contract:** authenticated active-user lookup, role/status enforcement, prosumer endpoints, and mobile session/token persistence.
- **Member 2 contract:** the shared slot entity now has a minimal allocation ledger and atomic capacity service; Member 2 must integrate active-reservation schedule/deactivation checks after branch synchronization.
- **Member 4 contract:** no completion implementation exists in available refs; reservation history/search representation, QR issuance/verification boundary, completion transition, and deployment expectations remain dependencies.
- **Runtime/configuration:** .NET SDK 10.0.401 and Docker are installed. The test runner provisions MongoDB without credentials or persistent test data. A safe JWT/development configuration is still needed for end-to-end HTTP verification.
- **Schema decisions:** lifecycle/status, UTC, and staff creation permissions are documented and mapped. Energy decimal precision/rounding, completion accounting, and Member 4 QR details still require confirmation.

## Verification status

| Check | Actual result |
| --- | --- |
| Repository instructions | No `AGENTS.md` or other repository instruction file found. |
| README/assignment/team plan | Read the only README. No assignment or team-plan artifact exists in the checkout, any available ref tree, or tracked document history. |
| Git branch | PASS: implementation is on `feature/reservation-workflow` after synchronizing it with `develop` at `f64b97f`. |
| Safe branch preparation | PASS: the clean reservation branch was fast-forwarded to current `develop`; Member 1's existing user API commit was integrated without reset or file discard. Commit, feature push, and develop merge were requested for this update. |
| Git baseline comparison | INFO: the baseline already contains the Component 3 API, tests, and reservation list/detail UI; this update extends the existing creation entry point. |
| Backend/framework inspection | PASS: ASP.NET Core controller API targeting `net10.0` confirmed from project/source. |
| Web foundation | IMPLEMENTED: React/TypeScript with Vite, Bootstrap 5, React Router, centralized Fetch client, environment API URL, `POST /api/auth/login`, `GET /api/auth/me`, session context, protected/role routes, responsive shared layout, and reusable loading/empty/error/confirmation/status components. Dashboard and Prosumer data remain unintegrated because their controllers are empty. |
| Web reservation UI | PASS: list/detail routes consume authorized DTOs and preserve list filters. Staff creation searches a bounded eligible-Prosumer endpoint, selects API-backed active stations/available slots, requires the slot model's kWh quantity, reviews before submit, blocks double submission, reuses one idempotency key for equivalent retries, reconciles uncertain requests through the same POST/key, preserves inputs after errors, explains common domain conflicts, and shows a dedicated saved summary with list/detail navigation. |
| Web dependency install | PASS: 185 packages audited with 0 reported vulnerabilities. TypeScript remains on the supported 6.x line because current `typescript-eslint` does not accept TypeScript 7. |
| Web checks | PASS: `npm run check` completed ESLint, **14/14** focused Vitest checks, TypeScript project compilation, and the Vite 8.3.1 production build. Vite transformed 62 modules and emitted the production bundle. |
| Android inspection | BLOCKED/ABSENT: no Android application is tracked in any available ref, so a screen-level Android verification cannot honestly be performed. The real-MongoDB owner-visibility test proves a staff-created reservation is returned by the target Prosumer's object-scoped list API—the REST contract an Android client must use. |
| Eligible Prosumer lookup | PASS: `GET /api/users/eligible-prosumers` is limited to Backoffice/GridOperator, requires a bounded search term, filters active Prosumer role/status in MongoDB before stable paging, and returns a minimal selection DTO. |
| MongoDB inspection | PASS: reservation collection mapping, BSON attributes, references, UTC fields, Decimal128 energy quantity, versioning, query/duplicate indexes, allocation claims, and per-Prosumer scheduling leases are implemented and exercised against MongoDB 8. |
| Reservation domain/DTO implementation | PASS (compile verified): entity/status/history/capacity state, request/query/response DTOs, collection mapping, and indexes are implemented. |
| Reservation creation API | PASS (compile verified): Prosumer self-create and Backoffice/assigned-Grid-Operator staff create validate current identities, eligibility, references, schedule, inclusive seven-day horizon, quantity, duplicate/overlap, atomic availability, Pending state, exact-once hold, hashed idempotency, and actor-derived allowed actions. |
| Reservation update API | PASS (compile/static verification): owner, Backoffice, and assigned-Grid-Operator update enforce editable status, current-start twelve-hour notice with inclusive boundary, target schedule/horizon/capacity/overlap rules, expected version, server-owned fields, Approved-to-Pending reset, same-slot deltas, target-first moves, audit data, and safe replay. |
| Focused update checks | PASS: eight real MongoDB tests verify valid updates, inclusive twelve-hour notice, final/stale/destination conflicts, rollback preservation, and Approved-to-Pending reset. |
| Reservation cancellation API | PASS (compile/static verification): owner, Backoffice, and assigned-Grid-Operator cancellation enforces object scope, Pending/Approved status, current-start twelve-hour notice with inclusive boundary, expected version, final `Cancelled` state, timestamp/actor/reason/history audit, actor-scoped idempotent replay, exact-claim release, and transaction/standalone reconciliation. The status/version/held-state compare-and-swap prevents a conforming completion mutation from also succeeding. |
| Focused cancellation checks | PASS: real MongoDB tests verify valid cancellation, exact-once release, replay safety, final-state blocking, object authorization, and cancellation-versus-completion CAS. |
| Reservation approval/rejection APIs | PASS (compile/static verification): Backoffice and assigned-Grid-Operator approval rechecks active Prosumer/station, slot snapshot/schedule/future/horizon, overlap, and one exact claim without a second hold. Backoffice rejection requires a bounded reason, records audit/history, and uses the exact-once release workflow. Both enforce Pending/version/held-state CAS, stable idempotency outcomes, terminal-state conflicts, and authoritative QR/action projections. |
| Focused approval/rejection checks | PASS: real MongoDB tests verify decision authorization, exact-once rejection release/replay, final-state blocking, Approved update reset, and approve-versus-reject concurrency. |
| Reservation read APIs | PASS (compile/static verification): `GET /api/reservations` applies owner/global/assigned-station scope in MongoDB before count/paging; supports view, status, station, Prosumer, UTC range, Backoffice search, stable newest-first sorting, and batch display enrichment including Prosumer names and the server-recorded request time. `GET /api/reservations/{id}` combines ID and actor scope and returns 404 when absent or out of scope. |
| Automated tests | PASS: `scripts/run-component3-tests.ps1` executed the complete suite: **39 passed, 0 failed, 0 skipped** in 6 seconds. This includes 31 real-MongoDB integration facts plus 8 focused read-policy facts; no unavailable Android test was counted as passed. |
| Confirmed defect fixed | PASS: concurrent scheduling-lease upserts can surface duplicate key code 11000 as `MongoCommandException`; the lease now treats that result as contention and retries, allowing update-versus-cancel and approve-versus-reject races to resolve through lifecycle/version CAS. |
| User API error mapping | PASS: Member 1's `BadRequestException` is now translated to HTTP 400 by shared middleware instead of falling through to HTTP 500. |
| Current-branch backend build | PASS: merged Member 2 and Component 3 source builds with .NET SDK 10.0.401 with 0 compilation errors. NuGet emits one `NU1900` warning because vulnerability metadata cannot be reached. |
| Component 3 integration build | PASS: `dotnet build SolarMicrogrid.slnx --configuration Release --no-restore -m:1` completed with 0 errors; only `NU1900` was emitted because the NuGet vulnerability feed was unreachable. |
| Assignment comment condition | PASS (static): every new/modified C# file has the required header block and every added/modified method begins with an explanatory inline comment. |
| MongoDB runtime verification | PASS: a real single-node replica set exercised transactions, persistence, unique indexes, allocation compare-and-swap behavior, idempotent replay, rollback, and competing lifecycle mutations. The disposable container and isolated database were removed after the run. |
| HTTP/JWT end-to-end verification | BLOCKED: no sanitized JWT development configuration or HTTP test accounts are supplied; service-level integration tests use authoritative persisted user records directly. |
| Secret review | ATTENTION: a non-empty JWT key is present in tracked base settings; value not reproduced. Ownership/rotation/removal needs confirmation. |

## Assignment evidence needed

### Inputs required from the team/course

- Original assignment brief, marking rubric, Component 3 acceptance criteria, and current team plan/ownership matrix.
- Android repository/project if it is maintained separately.
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

Commit message requested for this staff creation update:

`feat(web): add staff reservation creation workflow`
