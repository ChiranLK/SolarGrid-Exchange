# Member 4: Dashboard, QR Verification and Deployment Contracts

Contract date: 2026-09-27

Status: dashboard/history contract implemented; QR, completion, and deployment remain proposed where unresolved cross-owner decisions are explicitly marked `Blocked` or `Not Verified`

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

An eligible reservation is a server-authorized candidate for QR issuance, not merely a client-visible `Approved` label. The final rule must require all of the following:

1. The authenticated actor is the owning active `Prosumer`.
2. The reservation is in the actor's authorized scope and `Status == Approved`.
3. The token binds the reservation's current `Version` and station.
4. Server time is within the agreed issuance/check-in window.
5. The reservation/version has not been completed, cancelled, updated, revoked, or superseded.

The exact issuance/check-in window is `Blocked` because the assignment/team plan provides no value. Until it is agreed, the existing `QrEligible = Status == Approved` and `AllowedActions.CanGetQr` are only preliminary candidate flags and are `Not Verified` as final Member 4 eligibility.

### Verified transaction

A verified transaction is a short-lived, server-stored verification receipt produced only after an assigned active `GridOperator` submits a valid, unexpired, unconsumed QR payload and the API re-reads the reservation. It records at minimum:

- opaque verification ID;
- reservation ID and exact reservation version;
- station ID;
- verifying operator NIC on the server (never inside the QR payload);
- verified and expiry UTC timestamps;
- receipt state (`Available`, `Consumed`, or `Expired`); and
- token-identifier digest/replay evidence, never the raw QR value.

Verification does not itself change the reservation to `Completed` under this proposed two-step contract. The receipt is bound to one operator, station, reservation, and version and can authorize one completion only.

### Completed transfer

A completed transfer is the result of an atomic, idempotent `Approved -> Completed` mutation by the assigned active `GridOperator` using an available verification receipt and the current expected reservation version. The operation:

- consumes the receipt once;
- increments the reservation version once;
- sets `CompletedAtUtc`, `CompletedByActorNic`, and `CompletedVerificationId` from server data;
- appends one status-history entry;
- makes every QR for the reservation/version unusable; and
- returns the authoritative `ReservationResponseDto`.

The shared contract proposes changing the held allocation to `Consumed` without restoring it to available slot capacity. Final accounting is `Blocked` pending explicit agreement between Members 2, 3, and 4.

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

### `ReservationQrResponseDto`

| Property | Type | Rule |
| --- | --- | --- |
| `ReservationId` | string | Authorized reservation ObjectId. |
| `ReservationVersion` | long | Version at issue time. |
| `QrPayload` | string | Opaque or signed, compact value rendered by clients without interpretation. |
| `IssuedAtUtc` | `DateTime` | Server issue time. |
| `ExpiresAtUtc` | `DateTime` | Server expiry time. |

The QR claim set may contain only non-PII protocol fields needed for verification: random token identifier, reservation ID, reservation version, station ID, issued time, expiry, and cryptographic integrity data. It must never contain a JWT, bearer token, NIC, name, email, phone, address, credentials, signing secret, or editable business outcome.

### `VerifyReservationQrRequestDto`

| Property | Type | Rule |
| --- | --- | --- |
| `QrPayload` | string | Required, bounded opaque value from the scanner. No actor/station identity is accepted from the body. |

### `VerifyReservationQrResponseDto`

| Property | Type | Rule |
| --- | --- | --- |
| `VerificationId` | string | Opaque one-time receipt ID. |
| `ReservationId` | string | Re-read authorized reservation. |
| `ReservationVersion` | long | Version verified and required for completion. |
| `VerifiedAtUtc` | `DateTime` | Server verification time. |
| `ExpiresAtUtc` | `DateTime` | Receipt expiry. |
| `Reservation` | `ReservationResponseDto` | Authorized current summary; no raw token or signing data. |

### `CompleteReservationRequestDto`

| Property | Type | Rule |
| --- | --- | --- |
| `ExpectedVersion` | long | Positive and equal to the verified/current approved version. |
| `VerificationId` | string | Required opaque, available, unexpired receipt owned by the authenticated operator. |

## API routes

All routes require the existing JWT bearer authentication. Exact roles use the repository names.

| Method and route | Role and object scope | Request | Success |
| --- | --- | --- | --- |
| `GET /api/dashboard?recentLimit=5` | `Prosumer` own; `GridOperator` assigned station; `Backoffice` global | Query only | 200 `DashboardResponseDto` |
| `GET /api/dashboard/history?search=...&status=...&stationId=...&fromUtc=...&toUtc=...&page=1&pageSize=20` | `Prosumer` own; `GridOperator` assigned station; `Backoffice` global | `BookingHistoryQueryDto` query | 200 `PagedBookingHistoryResponseDto` |
| `GET /api/reservations/{reservationId}/qr` | Owning active `Prosumer` only | No body | 200 `ReservationQrResponseDto` |
| `POST /api/reservations/qr/verify` | Active `GridOperator`, assigned station only | `VerifyReservationQrRequestDto`; required `Idempotency-Key` | 200 `VerifyReservationQrResponseDto` |
| `POST /api/reservations/{reservationId}/complete` | Active `GridOperator`, assigned station only | `CompleteReservationRequestDto`; required `Idempotency-Key` | 200 existing `ReservationResponseDto` |

Backoffice may see dashboard/history data but may not obtain a Prosumer QR, verify it, or complete a transfer. A Prosumer may not verify or complete. Web versus Android never changes permission.

The verification route uses one idempotency key per logical scan. A retry with the same actor/key/fingerprint may return the same receipt; reuse with different input conflicts. A different request after the QR token was consumed is a replay conflict. Completion follows the repository's existing expected-version and actor-scoped idempotency patterns.

## HTTP outcomes and error format

### Status codes

| Status | Member 4 meaning |
| --- | --- |
| 200 | Successful dashboard read, QR issue, verification, or completion; an idempotent retry may return the original result with `Idempotency-Replayed: true`. |
| 400 | Malformed ObjectId, invalid/bounded query, malformed QR/request, invalid expected version shape, missing/invalid idempotency key, or model-validation failure. |
| 401 | Missing, invalid, or expired bearer JWT. |
| 403 | Authenticated role is not permitted, Grid Operator has no valid assigned station, or known object is outside staff station scope. |
| 404 | Authorized resource is absent; also used by existing reservation reads to hide another Prosumer's object. |
| 409 | Reservation is ineligible/stale/final, QR is expired/revoked/replayed/wrong-version, receipt is expired/consumed/mismatched, expected version is stale, invalid transition, or idempotency key conflicts. |
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
4. QR and receipt lifetimes are server configuration with validated positive bounds. The exact values remain `Blocked`; clients render `ExpiresAtUtc` and never extend expiry locally.
5. A material reservation update changes an approved reservation to `Pending` and changes its version, invalidating every prior QR/receipt. Cancellation, rejection, and completion also invalidate outstanding artifacts.
6. Verification checks active operator status and exact assigned station, approved status, current version, time window, QR expiry/revocation, and unique token consumption in one authoritative workflow.
7. QR replay protection uses a random high-entropy token identifier and a unique server-side digest/consumption record. Raw QR values are never persisted or logged.
8. A verification receipt is high entropy, short-lived, server stored, bound to operator/station/reservation/version, and consumed once with completion.
9. Completion atomically compares `Approved` status, expected version, station scope, receipt state/bindings, and expiry; consumes the receipt and updates reservation audit/history exactly once. Concurrent/repeated completion cannot win twice.
10. Idempotency stores scoped hashes/fingerprints rather than raw keys. A matching retry returns the persisted result; mismatched reuse is 409.
11. MongoDB unique indexes/CAS filters, not process memory or client state, enforce replay and one-time completion. Deployment must use the repository's transaction-capable path or test the existing compensation strategy against the chosen topology.
12. General reservation/dashboard responses never include raw QR payloads, token digests, signing material, or internal replay records.
13. HTTPS is mandatory outside local debug. Android cleartext remains debug-only; production web should use a same-origin `/api` reverse proxy unless a narrowly scoped API CORS policy is explicitly configured.
14. MongoDB connection strings, JWT/QR signing secrets, web domains, Android release keys, and deployment credentials come from environment/secret storage and are never committed.
15. Logs, analytics, screenshots, and test fixtures must redact JWTs, QR payloads, signing secrets, credentials, and personal data.

## Persistence and index boundary

Member 4 may add replay/receipt collections through the existing `MongoDbContext` and `MongoSettings`. Suggested server-only records are:

- QR token record: token-identifier digest, reservation ID/version, station ID, issue/expiry, consumed/revoked timestamps, and concurrency version.
- verification receipt: opaque ID/digest, reservation ID/version, station ID, operator NIC, verified/expiry/consumed timestamps, state, and idempotency hashes.

Required database guarantees are unique token digest, unique verification ID/digest, an index supporting reservation/version invalidation, a TTL cleanup index for expired artifacts, and compare-and-swap filters for consumption. TTL deletion is cleanup only; every request must explicitly compare expiry because MongoDB TTL removal is asynchronous.

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
- QR signing/key-rotation strategy and lifetimes;
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
| QR issue/verify/complete route and DTO proposal | Completed | Matches Component 3 boundary; implementation Not Started. |
| No PII/JWT in QR | Completed | Contracted; implementation/test Not Started. |
| Server-authoritative decisions | Completed | Contracted; implementation/test Not Started. |
| Replay and one-time completion design | Completed | Contracted; persistence implementation Not Started. |
| QR and verification receipt lifetime values | Blocked | No official/team value supplied. |
| Check-in/completion window | Blocked | No official/team rule supplied. |
| Pending reservation at/after start | Blocked | Shared contract lists this as an unresolved team decision. |
| Completed allocation accounting | Blocked | Requires cross-owner confirmation. |
| Existing `QrEligible` as final eligibility | Not Verified | It currently checks `Approved` status only. |
| Dashboard implementation and test coverage | Completed | API, DTOs, indexes, controller contracts, and MongoDB integration cases are implemented. |
| Dashboard MongoDB integration execution | Not Verified | The local replica set was unavailable; Mongo-independent controller/policy tests pass. |
| QR client/API implementation/tests | Not Started | Later prompt. |
| Completion implementation/tests | Not Started | Later prompt. |
| Deployment implementation/smoke test | Blocked | Target, domains, secrets, topology, and signing inputs absent. |
