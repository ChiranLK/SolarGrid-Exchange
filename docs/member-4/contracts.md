# Member 4: Dashboard, QR Verification and Deployment Contracts

Contract date: 2026-09-27

Status: dashboard/history and secure QR transaction contracts implemented; deployment remains proposed where unresolved cross-owner decisions are explicitly marked `Blocked` or `Not Verified`

## Authority and ownership

The ASP.NET Core API is the only authority for identity, role/station scope, server time, reservation state/version, dashboard counts, QR eligibility, verification, and completion. Web and Android clients render API responses and submit opaque values; they must not infer or recreate lifecycle, time-window, authorization, capacity, or replay decisions.

This contract extends the existing API, React application, and pure-native Java/XML Android application. It does not introduce another server, database abstraction, web app, Android app, authentication flow, user/session store, station/slot model, or reservation workflow.

- Member 1 remains owner of users, JWT/roles, authentication, and Android session storage.
- Member 2 remains owner of stations, slots, schedules, stored GPS/map data, and station/slot management.
- Member 3 remains owner of reservation creation/update/cancellation/approval/rejection, scheduling rules, duplicate/overlap rules, capacity holds/releases, and those workflow screens.
- Member 4 consumes those contracts for dashboards, history/search presentation, QR issue/display/verification, verified transactions, completion integration, and deployment.

## Shared definitions

All time comparisons use one `serverNowUtc` captured by the API for the operation. Client clocks are display-only.

### Current reservation

An `Approved` reservation whose interval contains server time:

```text
Status == Approved
AND ScheduledStartTimeUtc <= serverNowUtc
AND ScheduledEndTimeUtc > serverNowUtc
```

This is the existing `ReservationListView.Current` definition. The end is exclusive.

### Pending reservation

A reservation awaiting a staff decision and not already ended:

```text
Status == Pending
AND ScheduledEndTimeUtc > serverNowUtc
```

This is the existing `ReservationListView.Pending` definition. A pending reservation may already have started; the repository has no agreed automatic expiry/rejection process, so that operational case is `Blocked` pending a team decision.

### Approved future reservation

An approved reservation that has not started:

```text
Status == Approved
AND ScheduledStartTimeUtc > serverNowUtc
```

This is the existing `ReservationListView.ApprovedFuture` definition.

### Booking history

The existing `ReservationListView.History` includes:

```text
Status in [Cancelled, Rejected, Completed]
OR
(Status in [Pending, Approved] AND ScheduledEndTimeUtc <= serverNowUtc)
```

History is a read projection, not a new status or collection. Web and Android must continue to query the shared reservation endpoint rather than copy records locally.

### QR eligibility

An eligible reservation is a server-authorized candidate for QR issuance, not merely a client-visible `Approved` label. The implemented rule requires all of the following:

1. The authenticated actor is the owning active `Prosumer`.
2. The reservation exists in the actor's owner-scoped MongoDB query.
3. The reservation is `Approved`, its capacity state is `Held`, and its scheduled end is after server time.
4. The token record binds the reservation's current `Version`, station, and a one-way owner-reference hash.
5. The referenced Member 2 station still exists and is active.
6. The reservation/version has not been completed, cancelled, updated, revoked, or superseded.

No narrower pre-start check-in window was supplied. To avoid inventing one, issuance is allowed for approved future/current reservations but not after `ScheduledEndTimeUtc`. The older `QrEligible = Status == Approved` projection remains broader than the transaction API rule and clients must treat successful issuance as authoritative.

### Verified transaction

A verified transaction is the same server-stored transaction record after an assigned active `GridOperator` submits a valid, unexpired, unconsumed opaque QR token and the API re-reads the reservation. It records:

- only a hash of the returned opaque verification ID;
- reservation ID and exact reservation version;
- station ID;
- a one-way verifying-operator reference hash (never inside the QR payload);
- verified and expiry UTC timestamps;
- state (`Issued`, `Verified`, `Completed`, `Expired`, or `Revoked`); and
- token hash/replay evidence, never the raw QR or verification value.

Verification does not change the reservation to `Completed`. The returned receipt is bound to one operator, station, reservation, and version and can authorize one explicit completion only.

### Completed transfer

A completed transfer is the result of an atomic `Approved -> Completed` mutation by the same assigned active `GridOperator` using an unexpired verified receipt and the current expected reservation version. The operation:

- consumes the receipt once;
- increments the reservation version once;
- sets `CompletedAtUtc`, `CompletedByActorNic`, and an internal transaction audit reference in `CompletedVerificationId` from server data;
- appends one status-history entry;
- makes every QR for the reservation/version unusable; and
- returns the safe `CompleteQrTransactionResponseDto`.

The implemented transition changes the reservation capacity state to `Consumed` and retains the existing slot allocation; it does not restore delivered energy to available capacity.

## Dashboard contract

### Scope

Dashboard data is always constrained before aggregation:

| Role | Data scope |
| --- | --- |
| `Prosumer` | Reservations where `ProsumerNic` equals the authenticated NIC. |
| `GridOperator` | Reservations at the authenticated user's `AssignedStationId`; an absent/invalid assignment is 403. |
| `Backoffice` | All authorized reservations. |

Dashboard cards and lists use the same definitions above and the same reservation MongoDB collection. Counts must be database-derived; clients must not fetch all reservations and count/filter them locally.

### Implemented response DTOs

`DashboardResponseDto`:

| Property | Type | Meaning |
| --- | --- | --- |
| `ServerNowUtc` | `DateTime` | Time used for every view/count in this response. |
| `Role` | string | Exact authenticated API role. |
| `StationId` | string, nullable | Assigned station for a Grid Operator; otherwise null. |
| `Scope` | string | `OwnReservations`, `AssignedStation`, or `Global`. |
| `StatusSummary` | `DashboardStatusSummaryDto` | Exact status totals plus semantic view counts. |
| `CurrentReservations` | list of `DashboardReservationSummaryDto` | Bounded current list for Prosumers. |
| `PendingReservations` | list of `DashboardReservationSummaryDto` | Bounded pending list. |
| `RecentHistory` | list of `DashboardReservationSummaryDto` | Bounded scheduled-time-descending history list. |
| `RecentTransfers` | list of `DashboardReservationSummaryDto` | Staff-only approved/completed records, updated-time descending. |
| `ActiveTransfers` | list of `DashboardReservationSummaryDto` | Staff-only current transfers. |
| `CompletedTransfers` | list of `DashboardReservationSummaryDto` | Staff-only completed-time-descending records. |

`DashboardStatusSummaryDto`:

| Property | Type | Meaning |
| --- | --- | --- |
| `PendingTotal` | `long` | All persisted `Pending` records within actor scope. |
| `ApprovedTotal` | `long` | All persisted `Approved` records within actor scope. |
| `RejectedTotal` | `long` | All persisted `Rejected` records within actor scope. |
| `CancelledTotal` | `long` | All persisted `Cancelled` records within actor scope. |
| `CompletedTotal` | `long` | All persisted `Completed` records within actor scope. |
| `CurrentCount` | `long` | Current definition above. |
| `PendingCount` | `long` | Pending-view definition above; unlike `PendingTotal`, it excludes ended pending records. |
| `ApprovedFutureCount` | `long` | Approved-future definition above. |
| `HistoryCount` | `long` | History definition above. |

`DashboardReservationSummaryDto` contains the stable display projection: reservation ID/reference, Prosumer NIC/name, station ID/name/address, slot ID, scheduled start/end UTC, requested kWh, exact status, version, created/updated UTC, and optional completed UTC. It omits capacity/idempotency internals.

The dashboard endpoint accepts an optional `recentLimit` integer from 1 through 20, default 5.

### Implemented booking-history contract

`GET /api/dashboard/history` accepts `search`, nullable exact `status`, `stationId`, inclusive `fromUtc` and `toUtc`, `page` (minimum 1), and `pageSize` (1 through 100, default 20). Both date bounds apply to `ScheduledStartTimeUtc`; a supplied timestamp must include an explicit UTC/offset kind and `fromUtc` cannot be later than `toUtc`.

Search is trimmed, limited to 100 characters, regex-escaped, and matched case-insensitively against the displayed reservation reference, Prosumer NIC/name, and station name/address. The role scope and shared history predicate remain mandatory regardless of filters. Results sort by `ScheduledStartTimeUtc` descending and reservation ObjectId descending. The response includes `serverNowUtc`, `items`, `totalCount`, `page`, `pageSize`, and `totalPages`; a page beyond the result set is valid and empty.

## QR and verification DTOs

ASP.NET Core serializes the C# property names below as camelCase JSON.

### `IssueQrTransactionResponseDto`

| Property | Type | Rule |
| --- | --- | --- |
| `ReservationId` | string | Authorized reservation ObjectId. |
| `ReservationVersion` | long | Version at issue time. |
| `QrToken` | string | URL-safe, 256-bit random opaque value rendered by clients without interpretation. |
| `IssuedAtUtc` | `DateTime` | Server issue time. |
| `ExpiresAtUtc` | `DateTime` | Server expiry time. |

The QR contains only `QrToken` (or a client-created application deep link carrying only that token). Reservation/version/timestamps are response metadata outside the QR image. It must never contain a JWT, NIC, name, email, phone, address, reservation/station details, credentials, signing secret, or editable business outcome.

### `VerifyQrTransactionRequestDto`

| Property | Type | Rule |
| --- | --- | --- |
| `Token` | string | Required, 32-128 character URL-safe opaque value from the scanner. No actor/station identity is accepted from the body. |

### `VerifyQrTransactionResponseDto`

| Property | Type | Rule |
| --- | --- | --- |
| `VerificationId` | string | Opaque one-time receipt ID. |
| `ReservationId` | string | Re-read authorized reservation. |
| `ReservationVersion` | long | Version verified and required for completion. |
| `VerifiedAtUtc` | `DateTime` | Server verification time. |
| `ExpiresAtUtc` | `DateTime` | Receipt expiry. |
| confirmation fields | scalar values | Reservation reference/version, station ID/name, schedule, requested kWh, and exact approved status; no owner, token hash, or internal record fields. |

### `CompleteQrTransactionRequestDto`

| Property | Type | Rule |
| --- | --- | --- |
| `ExpectedVersion` | long | Positive and equal to the verified/current approved version. |
| `VerificationId` | string | Required opaque, available, unexpired receipt owned by the authenticated operator. |

`CompleteQrTransactionResponseDto` returns only the reservation ID/reference, station ID, `Completed` status, incremented version, and completion UTC timestamp.

## API routes

All routes require the existing JWT bearer authentication. Exact roles use the repository names.

| Method and route | Role and object scope | Request | Success |
| --- | --- | --- | --- |
| `GET /api/dashboard?recentLimit=5` | `Prosumer` own; `GridOperator` assigned station; `Backoffice` global | Query only | 200 `DashboardResponseDto` |
| `GET /api/dashboard/history?search=...&status=...&stationId=...&fromUtc=...&toUtc=...&page=1&pageSize=20` | `Prosumer` own; `GridOperator` assigned station; `Backoffice` global | `BookingHistoryQueryDto` query | 200 `PagedBookingHistoryResponseDto` |
| `POST /api/transactions/reservations/{reservationId}/qr` | Owning active `Prosumer` only | No body | 200 `IssueQrTransactionResponseDto` |
| `POST /api/transactions/verify` | Active `GridOperator`, assigned station only | `VerifyQrTransactionRequestDto` | 200 `VerifyQrTransactionResponseDto` |
| `POST /api/transactions/reservations/{reservationId}/complete` | Same active assigned `GridOperator` that verified | `CompleteQrTransactionRequestDto` | 200 `CompleteQrTransactionResponseDto` |

Backoffice may see dashboard/history data but may not obtain a Prosumer QR, verify it, or complete a transfer. A Prosumer may not verify or complete. Web versus Android never changes permission.

Scanning consumes the issued token exactly once; repeat scans return 409 rather than another receipt. Completion uses the receipt hash, expected reservation version, operator binding, reservation CAS, and transaction-state CAS. It is intentionally non-replayable: a repeat or concurrent losing request returns 409 rather than a second success.

## HTTP outcomes and error format

### Status codes

| Status | Member 4 meaning |
| --- | --- |
| 200 | Successful dashboard read, QR issue, verification, or completion. |
| 400 | Malformed ObjectId, invalid/bounded query, malformed opaque token/receipt, invalid expected version shape, or model-validation failure. |
| 401 | Missing, invalid, or expired bearer JWT. |
| 403 | Authenticated role is not permitted, Grid Operator has no valid assigned station, or known object is outside staff station scope. |
| 404 | Authorized resource is absent; also used by existing reservation reads to hide another Prosumer's object. |
| 409 | Reservation is ineligible/stale/final, QR is expired/revoked/replayed/wrong-version, receipt is expired/consumed/mismatched, expected version is stale, or the transition is invalid. |
| 500 | Unexpected server failure with no internal details exposed. |

To avoid token enumeration, scanner failures should use a generic safe message such as `The QR code is invalid or no longer usable.` Logs may retain a correlation ID and detailed reason but must not log the raw QR or JWT.

### Existing error shapes

Domain/service failures retain the shared middleware body:

```json
{
  "status": 409,
  "message": "The QR code is invalid or no longer usable."
}
```

Automatic ASP.NET model validation currently returns validation problem details with fields such as `status`, `title`, and `errors`. Web and Android already tolerate both conventions. A new machine-readable error code must not be introduced only for Member 4 without a shared API decision.

## Security and consistency rules

1. The API re-reads user, assigned station, reservation, version, status, and receipt for every sensitive operation. QR signature/decoding alone never authorizes an action.
2. Clients do not calculate eligibility, current state, completion windows, status transitions, or dashboard counts. They use returned flags, reasons, server timestamps, and refreshed DTOs.
3. QR data contains no PII, JWT, bearer token, password, credential, signing key, or trusted editable status/energy result.
4. QR and receipt lifetimes are server configuration validated from 1-30 minutes. Both default to five minutes; receipt expiry is additionally capped at the reservation's scheduled end. Clients render `ExpiresAtUtc` and never extend expiry locally.
5. A material reservation update changes an approved reservation to `Pending` and changes its version, invalidating every prior QR/receipt. Cancellation, rejection, and completion also invalidate outstanding artifacts.
6. Verification checks active operator status and exact assigned station, approved status, current version, time window, QR expiry/revocation, and unique token consumption in one authoritative workflow.
7. QR replay protection uses a random high-entropy token identifier and a unique server-side digest/consumption record. Raw QR values are never persisted or logged.
8. A verification receipt is high entropy, short-lived, server stored, bound to operator/station/reservation/version, and consumed once with completion.
9. Completion atomically compares `Approved` status, expected version, station scope, receipt state/bindings, and expiry; consumes the receipt and updates reservation audit/history exactly once. Concurrent/repeated completion cannot win twice.
10. MongoDB unique indexes/CAS filters, not process memory or client state, enforce replay and one-time completion. The reservation and transaction updates run in a MongoDB transaction on the supported replica-set topology; the existing standalone fallback still uses reservation-first CAS so only one request can win.
11. General reservation/dashboard responses never include raw QR payloads, token digests, signing material, or internal replay records.
12. HTTPS is mandatory outside local debug. Android cleartext remains debug-only; production web should use a same-origin `/api` reverse proxy unless a narrowly scoped API CORS policy is explicitly configured.
13. MongoDB connection strings, JWT secrets, web domains, Android release keys, and deployment credentials come from environment/secret storage and are never committed.
14. Logs, analytics, screenshots, and test fixtures must redact JWTs, QR payloads, credentials, and personal data.

## Persistence and index boundary

Member 4 uses the existing `MongoDbContext`/`MongoSettings` with one `QrTransactions` collection. Each record stores only:

- SHA-256 token hash and nullable SHA-256 verification-receipt hash;
- reservation ID/version, station ID, and one-way owner/operator reference hashes;
- issue/token-expiry, verification/receipt-expiry, completion, and update timestamps; and
- the exact transaction state.

Database guarantees are unique token hash, sparse unique verification hash, reservation/version/state lookup, expiry lookup, and compare-and-swap filters for verification/completion. Every request explicitly compares expiry; records are retained for replay/audit evidence rather than relying on asynchronous TTL deletion.

These records reference the existing reservation/station/user identifiers. They must not embed or duplicate user, station, slot, or reservation documents.

## Client integration boundary

### Web

- Reuse `apiRequest`, auth context, role routes, shared layout/components, reservation DTOs, and the existing Bootstrap green/gold theme.
- Implement dashboards in the existing `DashboardPage`/feature structure and operator verification/completion in `src/features/operations`.
- Render the QR as an opaque API value and never decode it for business decisions.
- Use the read-only Member 4 dashboard/history routes, which reuse Member 3's shared view predicates and never add reservation mutations.

### Android

- Reuse `AppContainer`, `ApiClient`, `ApiCallback`, `UiState`, Fragments/ViewModels, XML/Material resources, and the one SQLite session store.
- Extend the existing `OperatorTransactionRepository` and `nav_qr_operations` destination. Do not add Retrofit/Room/Compose/Kotlin or another session/network stack.
- Scanner UI sends only the scanned opaque payload. It must not declare a transfer verified/completed until the server response succeeds.
- Do not persist QR, verification receipt, or completed outcome as authoritative offline state and do not queue offline completion.

## Deployment contract and blockers

The eventual deployment must build the existing three deliverables:

1. `SolarMicrogrid.API` publish output with external Mongo/JWT/QR configuration and HTTPS/reverse-proxy headers.
2. `SolarMicrogrid.Web` production Vite bundle with the intended `/api` or trusted absolute API base URL.
3. Signed Android release artifact configured with the production HTTPS `/api/` base URL.

Before production deployment, the team must supply or decide:

- hosting provider/runtime and public domains;
- same-origin reverse proxy versus explicit trusted-origin CORS;
- transaction-capable MongoDB topology and backup/retention policy;
- secret manager/environment injection and rotation for the tracked JWT key;
- operational confirmation of the configured QR/verification lifetimes;
- TLS certificates, health/readiness checks, logging/monitoring, and rollback procedure;
- Android application signing identity and secure signing pipeline; and
- sanitized smoke-test accounts for all three roles.

These inputs are currently `Blocked`; no compatible deployment configuration can be finalized without inventing infrastructure that the repository/team has not selected.

## Contract traceability

| Contract item | Status | Notes |
| --- | --- | --- |
| Existing status enum and legal transitions | Completed | Reused unchanged. |
| Current/Pending/ApprovedFuture/History definitions | Completed | Reused from `ReservationReadPolicy`. |
| Role and object scopes | Completed | Reused from JWT roles, reservation read policy, and assigned station. |
| Dashboard route/DTO contract | Completed | Implemented at `GET /api/dashboard`. |
| Role-scoped history/search contract | Completed | Implemented at `GET /api/dashboard/history` with server-side filters/paging. |
| QR issue/verify/complete route and DTO contract | Completed | Implemented under `/api/transactions`. |
| No PII/JWT in QR | Completed | Opaque 256-bit token only; integration test verifies no owner/reservation/station data and hashed persistence. |
| Server-authoritative decisions | Completed | User, station, reservation, version, capacity, time, and transaction state are re-read by the API. |
| Replay and one-time completion design | Completed | Unique hashes, state/version CAS, MongoDB transaction, and race test implemented. |
| QR and verification receipt lifetime values | Completed | Configurable 1-30 minutes; both default to five minutes. |
| Check-in/completion window | Blocked | No official/team rule supplied. |
| Pending reservation at/after start | Blocked | Shared contract lists this as an unresolved team decision. |
| Completed allocation accounting | Completed | Reservation becomes `Consumed`; existing slot allocation is retained and not restored. |
| Existing `QrEligible` projection as final eligibility | Not Verified | It remains a preliminary status-only UI flag; the transaction API applies the authoritative stronger rule. |
| Dashboard implementation and test coverage | Completed | API, DTOs, indexes, controller contracts, and MongoDB integration cases are implemented. |
| Dashboard MongoDB integration execution | Completed | Included in the 60/60 full API suite against MongoDB 8 replica set. |
| QR transaction API implementation/tests | Completed | Full API suite passed 60/60 against MongoDB 8 replica set. Client QR UI remains Not Started. |
| Completion implementation/tests | Completed | Success, replay, expiry, changed-state, and simultaneous one-winner completion are covered and passing. |
| Deployment implementation/smoke test | Blocked | Target, domains, secrets, topology, and signing inputs absent. |
