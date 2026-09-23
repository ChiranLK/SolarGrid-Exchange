# Component 3: Reservation Lifecycle and API Contract

Contract date: 2026-09-23

Status: final for the stated Component 3 assignment rules; cross-team confirmations are listed at the end

## Purpose and authority

This document defines the shared reservation contract for the central ASP.NET Core Web API, the web client, and the native Android client. The API is the only authority for reservation validation, permissions, lifecycle transitions, capacity changes, time calculations, concurrency, and idempotency. Clients collect input, call the REST API, and display server results; they must not reproduce these business decisions locally.

No existing repository document conflicts with the decisions supplied for this contract. The repository contains only the preliminary scope audit in `docs/component-3/progress.md` and no assignment/team-plan artifact.

The same API routes serve both clients:

- Web must support create, update, and cancel through this API.
- Android must support create, update, and cancel through this API.
- After each successful Android create, update, or cancel operation, Android must navigate to a summary page built from the API response. The summary is presentation behavior, not a second source of business rules.

## Fixed business decisions

- Persist and compare backend timestamps in UTC.
- Capture one `serverNowUtc` at the beginning of each mutation and use it for every time comparison in that operation.
- A reservation uses an existing energy booking slot. Its scheduled start and end are copied from that slot.
- The selected slot must start strictly after `serverNowUtc`.
- The selected slot start must be on or before `serverNowUtc + 7 days`; exactly seven days is valid.
- Updating or cancelling requires the existing reservation start to be at least `serverNowUtc + 12 hours`; exactly twelve hours is valid.
- The twelve-hour test always uses the existing reservation's scheduled start, even when an update moves it to a later slot.
- `Pending` and `Approved` reservations hold slot energy capacity.
- A material update to an `Approved` reservation changes it to `Pending` and invalidates QR eligibility from the previous approval.
- `Cancelled`, `Rejected`, and `Completed` are final states.
- Repeating a mutation must never allocate or release capacity more than once.

The exact boundary expressions are:

```text
create/update target slot:
    targetStartUtc > serverNowUtc
    targetStartUtc <= serverNowUtc + 7 days

update/cancel existing reservation:
    existingScheduledStartUtc - serverNowUtc >= 12 hours
```

## Real station and slot capacity model

The current repository model has these distinct quantities:

| Concept | Existing field | Unit | Reservation use |
| --- | --- | --- | --- |
| Station generation rate | `SolarStationInfo.EnergyGenerationCapacityKw` | kW | Station metadata; not directly allocated by a reservation. |
| Station battery storage | `SolarStationInfo.BatteryStorageCapacityKwh` | kWh | Station metadata; not a physical connector count and not directly decremented per reservation. |
| Slot total energy | `EnergyBookingSlot.TotalCapacityKwh` | kWh | Upper bound for all energy allocated in that slot. |
| Slot available energy | `EnergyBookingSlot.AvailableCapacityKwh` | kWh | Authoritative remaining reservable energy. |
| Physical battery/connector count | Not present | count | Must remain a separate field and rule if the team later adds it. It must not be inferred from any kW/kWh value. |

Component 3 therefore reserves `RequestedEnergyKwh` as a positive decimal quantity. It must not create a physical slot-count field unless Member 2 introduces a separate count model and contract.

The implemented shared capacity mechanism adds a minimal `CapacityAllocations` ledger to each `EnergyBookingSlot`. Each entry stores only reservation ObjectId, reservation version, allocated kWh, and allocation UTC time. It does not duplicate the reservation, user, station, or slot document. The ledger is the authoritative evidence for how much a particular reservation may release; request payloads and reservation quantities alone never increment slot availability.

Capacity invariants:

```text
0 <= AvailableCapacityKwh <= TotalCapacityKwh
held energy = sum(RequestedEnergyKwh for Pending and Approved reservations)
AvailableCapacityKwh = TotalCapacityKwh - energy unavailable for new reservations
```

The last expression includes completed/consumed allocations if completion occurs after energy delivery; completion does not make consumed energy bookable again. This completion-accounting interpretation needs final confirmation from Members 2 and 4.

Slot availability is updated as follows after a successful capacity mutation:

- Zero available capacity changes a non-disabled slot to `FullyBooked`.
- Positive available capacity changes `FullyBooked` back to `Available`.
- A slot explicitly marked `Unavailable` stays `Unavailable`, including when cancellation or rejection releases energy.
- A create, quantity increase, or move into a slot requires that target slot to be `Available` with sufficient capacity.
- Cancelling or rejecting may release capacity even if the existing slot was subsequently marked `Unavailable`.
- Released capacity must never raise `AvailableCapacityKwh` above `TotalCapacityKwh`.

## Reservation persistence contract

### Status enum

`ReservationStatus` has exactly these values:

- `Pending`
- `Approved`
- `Rejected`
- `Cancelled`
- `Completed`

Values are stored as strings, following the existing user and slot enum convention.

### EnergyReservation fields

| Field | Storage/type | Required | Rule |
| --- | --- | --- | --- |
| `Id` | MongoDB ObjectId string | Yes | Server-generated reservation identifier. |
| `ProsumerNic` | string | Yes | Immutable target prosumer identity, derived from the authenticated actor on creation. |
| `StationId` | MongoDB ObjectId string | Yes | Copied from the selected slot and immutable except through a slot change. |
| `SlotId` | MongoDB ObjectId string | Yes | Selected Member 2 slot. |
| `ScheduledStartTimeUtc` | UTC `DateTime` | Yes | Snapshot of slot start used for notice, overlap, audit, and QR checks. |
| `ScheduledEndTimeUtc` | UTC `DateTime` | Yes | Snapshot of slot end used for overlap, audit, and QR checks. |
| `RequestedEnergyKwh` | Decimal128-backed `decimal` | Yes | Positive energy allocation; must fit target slot availability. |
| `Status` | string enum | Yes | Initial value is `Pending`. |
| `Version` | `long` | Yes | Starts at 1 and increments once per successful material mutation or status transition. |
| `CapacityState` | string enum | Yes | Internal consistency state: `Unallocated`, `HoldPending`, `Held`, `ReleasePending`, `Released`, `Consumed`, or `CompensationRequired`. |
| `CapacityClaimVersion` | `long` | Yes | Version of the canonical slot allocation claim; not client-controlled. |
| `CreatedAtUtc` | UTC `DateTime` | Yes | Set from server time. |
| `CreatedByActorNic` | string | Yes | Authenticated NIC; normally identical to `ProsumerNic`. |
| `UpdatedAtUtc` | UTC `DateTime` | Yes | Time of last successful mutation. |
| `UpdatedByActorNic` | string | Yes | Authenticated NIC responsible for the last mutation. |
| `StatusHistory` | embedded array | Yes | Append-only transition audit; first entry records creation as `Pending`. |
| `CompletedVerificationId` | string, nullable | No | Member 4 verification receipt used for a completed reservation; never contains the raw QR token. |

Each status-history entry contains `FromStatus` (null on creation), `ToStatus`, `ChangedAtUtc`, `ActorNic`, `ActorRole`, resulting `Version`, and an optional sanitized `Reason`.

Raw QR payloads, signing keys, access tokens, and plain idempotency keys must not be stored in the reservation document. Persistent idempotency receipts may store a key hash and request fingerprint in Component 3-owned infrastructure.

### Required reservation indexes

- `ProsumerNic + ScheduledStartTimeUtc` for own history and overlap checks.
- `SlotId + Status` for held-capacity and slot integration queries.
- `StationId + Status + ScheduledStartTimeUtc` for Grid Operator and dashboard queries.
- `Status + ScheduledStartTimeUtc` for Backoffice workflow queues.
- A unique index for the persistent idempotency-receipt scope/key hash.

An exact active duplicate must also be protected against races. The implementation may use a compatible partial unique index or the transaction-scoped prosumer scheduling guard described below; it must not rely only on a pre-insert query.

## Lifecycle and capacity effects

| Current status | Action | Actor | Required result | Capacity effect |
| --- | --- | --- | --- | --- |
| None | Create | Prosumer | `Pending`, version 1 | Allocate requested energy once. |
| `Pending` | Material update | Owning Prosumer | Remain `Pending`; increment version | Apply only the energy delta, or atomically move allocation between slots. |
| `Approved` | Material update | Owning Prosumer | Change to `Pending`; increment version; invalidate old QR eligibility | Apply only the delta/move; capacity remains held by the updated reservation. |
| `Pending` | Approve | Backoffice | `Approved`; increment version | None; capacity is already held. |
| `Pending` | Reject | Backoffice | `Rejected`; increment version | Release held energy once. |
| `Pending` | Cancel | Owning Prosumer | `Cancelled`; increment version | Release held energy once. |
| `Approved` | Cancel | Owning Prosumer | `Cancelled`; increment version; invalidate QR eligibility | Release held energy once. |
| `Approved` | Complete after current QR verification | Assigned Grid Operator through Member 4 flow | `Completed`; increment version | Do not restore consumed energy to bookable capacity. |
| `Rejected`, `Cancelled`, or `Completed` | Any mutation | Any actor | Reject as final-state conflict | None. |

There is no direct transition from `Approved` to `Rejected`. Backoffice must reject while the reservation is `Pending`. There is no direct transition from `Pending` to `Completed`; completion requires approval and a current successful QR verification.

A material update means a changed `SlotId` or changed `RequestedEnergyKwh`. The target prosumer is immutable. If an update request supplies the same values:

- A replay with the same idempotency key returns the stored original response.
- A new idempotency key produces a successful no-op response without changing status, version, timestamps, history, QR eligibility, or capacity.

## Permissions

| Capability | Prosumer | Backoffice | Grid Operator |
| --- | --- | --- | --- |
| Create reservation | Yes, for self only | Yes, for any eligible active Prosumer | Yes, for an eligible active Prosumer at the operator's assigned station |
| List/view | Own reservations only | All reservations with filters | Reservations for assigned station only |
| Update/reschedule | Own `Pending` or `Approved` reservation, subject to notice | Yes, on behalf of the owner without bypassing rules | Yes, only when both current and destination stations match the assigned station |
| Cancel | Own `Pending` or `Approved` reservation, subject to notice | No administrative override in this contract | No |
| Approve/reject | No | Yes, `Pending` only | No |
| Obtain/display QR | Own current `Approved` reservation | Read eligibility only | No |
| Verify QR | No | No | Yes, only for assigned station |
| Complete | No | No | Yes, only for assigned station and a current verification receipt |
| Dashboard/history/search | Own history only | Global authorized views | Assigned-station authorized views |

Web versus Android does not change permissions. Authorization comes from JWT claims, not from a client-provided platform flag.

## Actor identity and target prosumer identity

- `ActorNic` always comes from the authenticated `ClaimTypes.NameIdentifier` claim.
- `ActorRole` always comes from the authenticated role claim and must match the current active user record where the action is security-sensitive.
- Prosumer create/update/cancel request DTOs do not accept `ProsumerNic`, `ActorNic`, or role fields.
- On create, the API sets `ProsumerNic = ActorNic`; a Prosumer cannot create for another NIC.
- Staff creation uses a separate DTO with `TargetProsumerNic`; the API still derives `ActorNic` and role from JWT claims, validates both current user records, and records the staff actor separately from the target owner.
- On update/cancel, the API loads the reservation, compares its `ProsumerNic` to `ActorNic`, and never trusts a client-supplied target identity.
- Staff update uses the stored immutable `ProsumerNic`; the request cannot replace the owner. Backoffice is globally scoped, while a Grid Operator must match both the reservation's current station and the proposed slot's station.
- Backoffice action DTOs identify only the reservation; the target remains the immutable stored `ProsumerNic`.
- Grid Operator scope is checked using the actor's stored `AssignedStationId` against the reservation's `StationId`.
- Audit fields record the actor, while `ProsumerNic` continues to identify the reservation owner.
- For a Prosumer requesting another user's identifier, return 404 rather than revealing that the reservation exists. A valid reservation with an explicitly disallowed role/action returns 403.

## Validation rules

### Create

The API must validate, within the mutation transaction:

1. Authenticated actor is either an `Active` Prosumer creating for self, an `Active` Backoffice user, or an `Active` Grid Operator creating at their assigned station.
2. `Idempotency-Key` is present and valid.
3. `SlotId` is a valid ObjectId and the slot exists.
4. Slot's station exists and is active.
5. Slot status is `Available`.
6. Slot start is strictly in the future and no later than seven days from the captured server time.
7. `RequestedEnergyKwh > 0` and does not exceed current `AvailableCapacityKwh`.
8. No active exact duplicate or time overlap exists for this prosumer.
9. Capacity conditional update and reservation insert both succeed atomically.

Staff creation additionally requires that `TargetProsumerNic` resolves to an `Active` user whose stored role is `Prosumer`. Backoffice may use any active station. Grid Operator creation requires `User.AssignedStationId` to equal the slot's current `StationId`. The Prosumer route has no target-NIC property and always uses the authenticated NIC.

### Update/reschedule

The API must validate:

1. Actor is the owning active Prosumer, active Backoffice user, or active Grid Operator assigned to both the current and proposed station.
2. Reservation is `Pending` or `Approved`.
3. `ExpectedVersion` matches the stored version.
4. Existing `ScheduledStartTimeUtc - serverNowUtc >= 12 hours`.
5. Target slot/station exists, is active/available for any new or increased allocation, starts in the future, and starts no later than seven days from server time.
6. Requested energy is positive and the target has capacity for the required delta.
7. Updated interval does not duplicate or overlap another active reservation for the same prosumer, excluding the reservation being updated.
8. A changed slot/quantity is applied atomically. Moving slots reserves the target and releases the old allocation in the same transaction.
9. If prior status was `Approved`, status becomes `Pending`; the resulting version makes every QR tied to the prior version invalid.

The notice check is intentionally against the old scheduled start. Moving a reservation farther into the future does not bypass the twelve-hour rule.

Staff updates apply the same notice, horizon, status, overlap, capacity, version, and idempotency rules as owner updates. Staff have no emergency bypass. Another Prosumer receives a hidden-scope 404; unauthorized staff receive 403.

### Cancel

The API must validate:

1. Actor is the owning active Prosumer.
2. Reservation is `Pending` or `Approved`.
3. `ExpectedVersion` matches.
4. Existing `ScheduledStartTimeUtc - serverNowUtc >= 12 hours`.
5. Status change and capacity release commit once in the same transaction.

### Approve and reject

- Actor must be an active Backoffice user.
- Reservation must be `Pending`, and `ExpectedVersion` must match.
- Approval keeps capacity held and creates new current-version QR eligibility.
- Rejection requires a non-empty sanitized reason and releases capacity once.
- Approval/rejection after the scheduled start is not allowed. Handling a still-pending reservation that reaches its start requires the team confirmation listed below.

### Verify and complete

- Actor must be an active Grid Operator assigned to the reservation station.
- Reservation must be `Approved` and the QR must reference the reservation's current version.
- Verification must reject a replayed, expired, revoked, wrong-station, wrong-reservation, or older-version QR.
- Completion requires a current one-time verification receipt, matching `ExpectedVersion`, and a valid completion time window.
- Completion is atomic and idempotent and stores only the verification receipt identifier.

## Duplicate and overlap rules

`Pending` and `Approved` are the active statuses for duplicate, overlap, station-deactivation, and slot-mutation checks.

- A prosumer may not have two active reservations for the same `SlotId`.
- A prosumer may not have active reservations whose time intervals overlap, even at different stations.
- Intervals overlap when `newStartUtc < existingEndUtc` and `newEndUtc > existingStartUtc`.
- Adjacent intervals where one ends exactly when the other begins do not overlap.
- An update excludes its own reservation ID from duplicate/overlap checks.
- Final reservations do not block a new booking solely because their historical interval matches. Normal future/time/capacity rules still apply.
- Different prosumers may reserve the same energy slot until its kWh availability is exhausted.
- Client-side duplicate checks are advisory only; the server transaction is authoritative.

Concurrent operations for one prosumer are serialized with the Component 3-owned `ReservationSchedulingGuards` collection so that two requests cannot both pass an overlap query. Acquisition is an atomic missing-or-expired lease write keyed by normalized Prosumer NIC; release is conditional on the server-generated lease token. A TTL index removes abandoned leases, and the implementation never adds lock fields to Member 1's `User` document or relies on an in-memory lock.

## Concurrency, versioning, atomicity, and idempotency

### Optimistic versioning

- Every response includes `Version`.
- Every mutation of an existing reservation includes a required `ExpectedVersion`.
- The database mutation filter includes reservation ID, current status, and `Version == ExpectedVersion`.
- A successful material mutation/transition increments `Version` exactly once.
- A stale version returns 409 and performs no capacity change.

### Atomic capacity handling

The deployed MongoDB topology is not documented. The implemented strategy therefore prefers a supported transaction and has an explicit standalone-server compensation path. It never uses an in-memory lock as the consistency boundary.

Every slot mutation is independently safe through one MongoDB compare-and-swap operation:

- Hold filters by slot ID, matching station/start/end snapshot, `Available` status, exact current `AvailableCapacityKwh`, sufficient remaining energy, and absence of the reservation claim. The same update decrements capacity, records the claim, and derives slot status. An already-exact claim is accepted before snapshot revalidation so an interrupted post-hold creation can finish idempotently.
- Same-slot quantity change filters by the exact existing reservation ID/version/kWh claim plus exact current availability. The same update changes only the delta and advances the claim version.
- Release filters by the exact stored claim and exact current availability. The same update removes the claim and restores the claim's stored kWh—not a client-supplied quantity.
- A repeated hold returns `AlreadyApplied`; a repeated release returns `AlreadyReleased` and cannot increment capacity twice.
- Compare-and-swap contention reloads and retries a bounded number of times. It never falls through to an unconditional write.

`MongoTransactionRunner` runs multi-document reservation work with snapshot read concern, primary read preference, and majority write concern. A transaction includes, as applicable:

- Acquire/update the prosumer schedule guard.
- Claim or replay the idempotency receipt.
- Re-read reservation and slot state.
- Conditionally decrement/increment slot availability.
- Insert/update the reservation and append history.
- Update slot availability status.
- Commit all changes, or roll back all changes.

A target capacity decrement uses a conditional filter equivalent to `AvailableCapacityKwh >= requiredIncrease`. Updates use only the delta. Slot moves reserve the new slot and release the old slot in the same transaction. No client may call a capacity adjustment endpoint separately.

Fallback is selected only for MongoDB's definitive transaction-not-supported `IllegalOperation` response. Network failures, unknown commit results, and other errors are not blindly replayed as fallback operations.

When transactions are unavailable, the service uses these durable compensation rules:

- Create stores/uses one reservation identity and an idempotent slot claim. An uncertain post-hold failure retains that exact claim for the same-key retry; reconciliation never invents a missing hold.
- Creation first persists a `Pending` reservation in internal `HoldPending` capacity state with the hashed key/fingerprint, then applies the idempotent slot claim and advances the reservation to `Held`. A retry resumes the same reservation identity. If a definite pre-hold failure occurs, only an unallocated provisional document is removed; an uncertain result with a persisted claim is retained for safe retry rather than risking a double allocation or release.
- Moving slots holds the target first, performs a version-filtered reservation compare-and-swap, then releases the old claim. If the compare-and-swap fails or its result is uncertain, reconciliation reads the persisted reservation, retains its canonical slot claim, and releases only non-canonical claims.
- A same-slot increase is held before the reservation compare-and-swap; a failed compare-and-swap reconciles back to the persisted quantity. A same-slot decrease is persisted before energy is freed, so failure preserves the original larger allocation.
- Cancel/reject workflows will record release-pending/final state, remove the exact slot claim, then mark `Released`. A crash is repaired by `ReconcileCapacityAsync`; claim absence makes release retries harmless.
- Completed reservations retain their canonical claim as consumed capacity and use `Consumed`, not `Released`.
- `CompensationRequired` records an inconsistency that cannot be repaired safely without operator review. The repair path never allocates missing capacity speculatively.

This compensation strategy is safe but can temporarily over-reserve capacity between steps. Transaction-capable replica-set or sharded deployments remain strongly preferred.

### Idempotency

The following mutation routes require an `Idempotency-Key` header:

- Create, update, and cancel.
- Approve and reject.
- QR verify and complete.

Keys are scoped to authenticated actor NIC, HTTP method, canonical route/action, and key value.

Creation keys must contain 8 through 200 printable characters. The database stores only a SHA-256 scope/key hash plus a canonical request-fingerprint hash; the raw key is never persisted.

- Same key and same canonical request fingerprint: return the stored original HTTP status and response without rerunning capacity logic. The API may add `Idempotency-Replayed: true`.
- Same key with a different request fingerprint: return 409.
- New key against a now-final or stale reservation: validate normally and return the applicable 409; do not release/allocate again.
- A mutation is not reported successful until its idempotency receipt, reservation change, and capacity change have committed together.

The implemented update path stores the latest scoped update-key hash and canonical request-fingerprint hash on the reservation in the same version-filtered mutation. An immediate same-key/same-request retry reconciles any interrupted fallback capacity step and returns the current saved summary without allocating twice; a changed fingerprint returns 409. Once a later successful update replaces these latest-update fields, replaying an older request fails the normal stale-version check rather than mutating capacity again.

## DTO contracts

Property names below are C# names. ASP.NET Core serializes them as camelCase JSON under the existing web defaults.

### CreateReservationRequestDto

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `SlotId` | string | Yes | MongoDB ObjectId. Station and schedule are derived from this slot. |
| `RequestedEnergyKwh` | decimal | Yes | Must be positive and within available slot energy. |

The request does not contain a prosumer NIC, station ID, status, time, or capacity result.

### StaffCreateReservationRequestDto

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `TargetProsumerNic` | string | Yes | Eligible Prosumer NIC; never used as the acting identity. |
| `SlotId` | string | Yes | MongoDB ObjectId; Grid Operator scope is derived from its station. |
| `RequestedEnergyKwh` | decimal | Yes | Positive kWh quantity under the same slot-capacity rules as self-create. |

### UpdateReservationRequestDto

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `SlotId` | string | Yes | New or unchanged target slot. |
| `RequestedEnergyKwh` | decimal | Yes | New or unchanged requested energy. |
| `ExpectedVersion` | long | Yes | Must be positive and equal stored version. |

### CancelReservationRequestDto

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `ExpectedVersion` | long | Yes | Optimistic concurrency token. |
| `Reason` | string | No | Trimmed, sanitized audit note; proposed maximum 500 characters. |

### ApproveReservationRequestDto

| Property | Type | Required |
| --- | --- | --- |
| `ExpectedVersion` | long | Yes |

### RejectReservationRequestDto

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `ExpectedVersion` | long | Yes | Optimistic concurrency token. |
| `Reason` | string | Yes | Non-empty, trimmed, sanitized; proposed maximum 500 characters. |

### CompleteReservationRequestDto

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `ExpectedVersion` | long | Yes | Must match current approved reservation version. |
| `VerificationId` | string | Yes | One-time receipt returned by Member 4 QR verification. |

### Member 4 QR DTO boundary

`ReservationQrResponseDto` contains `ReservationId`, `ReservationVersion`, opaque/signed `QrPayload`, `IssuedAtUtc`, and `ExpiresAtUtc`.

`VerifyReservationQrRequestDto` contains only the scanned `QrPayload`. Actor/station identity comes from authentication, never from the QR request body.

`VerifyReservationQrResponseDto` contains a one-time `VerificationId`, `ReservationId`, `ReservationVersion`, `VerifiedAtUtc`, `ExpiresAtUtc`, and an authorized `ReservationResponseDto` summary. The receipt is short-lived and bound to the verifying Grid Operator, reservation, version, and station.

### ReservationListQueryDto

| Property | Type | Default | Scope |
| --- | --- | --- | --- |
| `Status` | nullable enum | null | All roles within their authorized data scope. |
| `StationId` | nullable string | null | Backoffice; Grid Operator must match assigned station. |
| `ProsumerNic` | nullable string | null | Backoffice only; ignored/rejected for other roles. |
| `FromUtc` | nullable UTC DateTime | null | Filter scheduled start at or after value. |
| `ToUtc` | nullable UTC DateTime | null | Filter scheduled start at or before value. |
| `Search` | nullable string | null | Backoffice-authorized identifier search; exact fields must avoid sensitive-data leakage. |
| `Page` | int | 1 | Minimum 1. |
| `PageSize` | int | 20 | Range 1 through 100. |

### ReservationResponseDto

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | string | Reservation ObjectId. |
| `ProsumerNic` | string | Owner; only returned within authorized scope. |
| `StationId` | string | Derived station identifier. |
| `SlotId` | string | Selected energy slot. |
| `ScheduledStartTimeUtc` | UTC DateTime | Stable reservation schedule snapshot. |
| `ScheduledEndTimeUtc` | UTC DateTime | Stable reservation schedule snapshot. |
| `RequestedEnergyKwh` | decimal | Held/consumed energy allocation. |
| `Status` | string | Enum name. |
| `Version` | long | Required by the next mutation. |
| `QrEligible` | bool | Derived: current status is `Approved`; final issuance rules remain Member 4-owned. |
| `AllowedActions` | object | Server-derived actor-scoped booleans for update, cancel, approve, reject, QR retrieval/verification, and completion. |
| `CreatedAtUtc` | UTC DateTime | Server timestamp. |
| `UpdatedAtUtc` | UTC DateTime | Server timestamp. |
| `LastStatusReason` | nullable string | Authorized, sanitized latest transition reason. |

Create, update, cancel, approve, reject, and complete all return the current `ReservationResponseDto`. This makes Android's required post-mutation summary deterministic without a client-side status calculation. The Android summary must show at least reservation ID, status, station/slot reference, scheduled UTC time rendered in the chosen UI time zone, requested kWh, and the server response outcome.

### PagedReservationResponseDto

Follows the existing station/slot convention:

- `Items: IReadOnlyList<ReservationResponseDto>`
- `TotalCount: long`
- `Page: int`
- `PageSize: int`
- `TotalPages: int`

## HTTP routes and responses

All routes require JWT authentication. Both web and Android use the Prosumer mutation routes below; there are no client-specific business endpoints.

| Method and route | Role/scope | Success | Important failures |
| --- | --- | --- | --- |
| `GET /api/reservations` | Role-scoped list | 200 paged response | 400 invalid query; 401; 403 invalid filter scope |
| `GET /api/reservations/{reservationId}` | Owner, Backoffice, or assigned Grid Operator | 200 | 400 invalid ID; 401; 403 role; 404 absent/out of scope |
| `POST /api/reservations` | Prosumer self | 201 with `Location` and response DTO | 400; 401; 403; 404 slot/station; 409 duplicate/overlap/capacity/idempotency |
| `POST /api/reservations/staff` | Backoffice globally; Grid Operator at assigned station | 201 with `Location` and response DTO | 400; 401; 403 role/station; 404 target/slot/station; 409 target eligibility/duplicate/overlap/capacity/idempotency |
| `PUT /api/reservations/{reservationId}` | Owning Prosumer, Backoffice, or Grid Operator scoped to current and destination station | 200 | 400; 401; 403; 404; 409 notice/status/version/overlap/capacity/idempotency |
| `POST /api/reservations/{reservationId}/cancel` | Owning Prosumer | 200 | 400; 401; 403; 404; 409 notice/status/version/idempotency |
| `POST /api/reservations/{reservationId}/approve` | Backoffice | 200 | 400; 401; 403; 404; 409 status/version/time/idempotency |
| `POST /api/reservations/{reservationId}/reject` | Backoffice | 200 | 400; 401; 403; 404; 409 status/version/time/idempotency |
| `GET /api/reservations/{reservationId}/qr` | Owning Prosumer, Member 4 implementation | 200 QR response | 401; 403; 404; 409 not eligible |
| `POST /api/reservations/qr/verify` | Assigned Grid Operator, Member 4 implementation | 200 verification receipt/summary | 400; 401; 403; 404; 409 invalid/replayed/expired/stale QR |
| `POST /api/reservations/{reservationId}/complete` | Assigned Grid Operator, Member 4 integration | 200 | 400; 401; 403; 404; 409 status/version/verification/idempotency |

No delete route exists. Audit records are retained and lifecycle actions change status.

## Error-response contract

Reservation endpoints preserve the existing shared API convention for service/domain failures:

```json
{
  "status": 409,
  "message": "The reservation changed. Reload it and try again."
}
```

Mapping:

- 400: malformed ObjectId/input, non-positive quantity, invalid UTC/query range, missing required idempotency key, or model validation failure.
- 401: missing/invalid/expired JWT.
- 403: authenticated actor lacks the required role or assigned-station scope.
- 404: station, slot, or authorized reservation view does not exist; also used to hide another prosumer's reservation.
- 409: duplicate/overlap, insufficient/stale capacity, invalid lifecycle transition, final state, notice/horizon conflict, stale version, or idempotency-key conflict.
- 500: unexpected server failure with no internal details.

ASP.NET `[ApiController]` validation currently returns standard validation problem details, while shared exception middleware returns `{status,message}`. A machine-readable `code` field would improve Android/web handling but would change the existing API convention; this remains a team confirmation rather than a Component 3-only change.

Clients may use response status for behavior and display the server message, but must not parse message text to recreate business rules.

## Member 4 QR and completion integration

Component 3 owns reservation status, version, eligibility, authorization boundary, and the atomic `Approved -> Completed` transition. Member 4 owns QR representation, signing/issuance, verification, replay protection, UI/scanner workflow, and completion endpoint integration.

Minimum QR contract:

- QR is available only for an `Approved` reservation at its current version.
- QR data binds at least reservation ID, reservation version, a one-time token identifier, issued time, expiry, and station context. It must be signed or opaque and must not trust editable clear-text fields.
- Material update, cancellation, rejection, completion, or any version mismatch invalidates prior QR eligibility.
- Verification re-reads the reservation; decoding a valid signature alone is insufficient.
- Verification enforces assigned-station scope and returns a one-time `VerificationId` rather than completing implicitly.
- Completion consumes that receipt once, checks the same reservation/version, and calls the Component 3 lifecycle/capacity transaction.
- Raw QR payloads and signing secrets are not stored in or returned by general reservation responses.

Whether QR verification should automatically complete instead of using the separate completion call, plus QR validity/check-in timing, still requires Member 4 confirmation.

## Cross-component data ownership

| Data/rule | Owner | Integration obligation |
| --- | --- | --- |
| User identity, JWT claims, role/status, activation | Member 1 | Component 3 reads authenticated NIC/role and active user state; it does not duplicate users. |
| Android session/token and SQLite cache | Member 1 | Android sends JWT and caches server DTOs only; cached state cannot authorize a mutation. |
| Station details, active state, location, operating schedule | Member 2 | Component 3 references station IDs and validates current station state. |
| Slot schedule, total/available kWh, availability state | Member 2 | Component 3 references the existing slot model; the shared slot entity now carries a minimal reservation allocation ledger used only by the atomic capacity service. |
| Reservation entity, lifecycle, ownership, time/notice rules, energy allocation transaction, idempotency/versioning | Member 3 | Single source of truth used by both clients and Member 4 integrations. |
| Reservation read/search contract | Member 3 API contract; Member 4 dashboard/UI consumption | Member 4 does not query MongoDB directly or reimplement lifecycle filters. |
| QR issue/format/signing/verification/replay controls | Member 4 | Must bind to Component 3 reservation ID/version/status and assigned station. |
| Completion UI/orchestration | Member 4 | Must invoke Component 3-owned completion transition; no direct reservation/slot writes. |
| Deployment/runtime support | Member 4/team | Must provide transaction-capable MongoDB and secret-safe API configuration. |

Member 2 slot changes must honor active reservations. While a `Pending` or `Approved` reservation references a slot:

- The slot start/end must not be changed independently; this would invalidate notice, overlap, and QR contracts.
- Total capacity must not be reduced below already allocated energy (the latest slot service already checks this).
- Disabling a slot needs an explicit operational policy for existing reservations; it must not silently cancel or release them.

Station deactivation must remain blocked while future `Pending` or `Approved` reservations exist.

## Client integration requirements

### Web

- Use the same authenticated create/update/cancel routes and DTOs as Android.
- Read `Version` from the response and submit it on subsequent mutation.
- Generate a new idempotency key for each user-intended operation and reuse it only when retrying that same operation.
- Render server validation/status results; do not calculate authoritative capacity, seven-day, twelve-hour, overlap, or transition decisions in the browser.

### Android

- Follow the same REST, version, and idempotency behavior as web.
- After a successful create, update, or cancel response, open a summary page using the returned `ReservationResponseDto`.
- Do not show a locally predicted status/capacity as final before the API returns.
- SQLite, if used, caches session/response data only. It must not become an offline mutation authority or independently allocate capacity.
- A retry after timeout reuses the same idempotency key so the summary can safely show the replayed server result.

## Team decisions still requiring confirmation

These items are not contradicted by repository code, but they cross ownership boundaries or are absent from the assignment details:

1. **Energy decimal precision and rounding:** confirm allowed `RequestedEnergyKwh` scale, minimum increment, maximum, and rounding policy. Existing fields use Decimal128-backed `decimal` but specify no precision rule.
2. **Approval owner:** this contract assigns `Pending -> Approved/Rejected` to Backoffice. Staff creation does not grant Grid Operators approval permission; confirm that Grid Operator remains excluded from approval.
3. **Grid Operator scope:** confirm that `User.AssignedStationId` is the authoritative single-station assignment and whether multiple-station assignment is needed.
4. **Slot edits with active reservations:** confirm with Member 2 that schedule changes are rejected while `Pending`/`Approved` reservations reference the slot, and define the operational response when a slot is disabled.
5. **Pending at start time:** decide whether an unapproved `Pending` reservation is automatically rejected, manually rejected, or handled by another documented process, and whether its now-unusable capacity remains consumed.
6. **Completion accounting/window:** confirm that `Completed` does not restore consumed energy, and define how early/late a Grid Operator may verify and complete.
7. **QR details:** Member 4 must confirm token format, expiry, one-time replay store, display/check-in window, and whether verify and complete remain separate operations.
8. **Error machine codes:** decide whether the shared API error body will remain `{status,message}` or gain a stable optional `code` across all components.
9. **Reason limits:** confirm the proposed 500-character cancellation/rejection reason maximum and any audit-retention requirements.
10. **MongoDB deployment:** confirm whether development/test/production use a transaction-capable replica set/sharded cluster or the implemented standalone compensation mode; run topology-specific integration and failure-injection tests before release.
11. **Administrative override:** Backoffice and assigned Grid Operators may perform ordinary on-behalf-of updates, but there is no emergency update/cancel or bypass of the seven-day/twelve-hour rules. Any override requires a separately authorized and audited team decision.
12. **Missing clients:** supply the actual web and Android projects before implementation so their framework, language, XML/Compose choice, session contract, and navigation patterns can be followed rather than guessed.

Suggested commit message if this document is later committed by the user:

`docs(reservations): define lifecycle rules and API contract`
