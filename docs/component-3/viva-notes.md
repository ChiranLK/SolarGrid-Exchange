# Component 3 viva notes

Plain explanations of the reservation workflow design, with the source to point at. The full rules are in `docs/component-3/reservation-contract.md`.

## 1. FAT service pattern (thin controller, fat service)

- `ReservationsController` only reads the JWT identity, passes the request and `Idempotency-Key` to `ReservationService`, and returns the result.
- Every rule lives in the services:
  - `ReservationService`: create, update, cancel, approve, reject, reads and response mapping;
  - `ReservationCapacityService`: slot capacity;
  - `ReservationSchedulingGuardService`: per-Prosumer lease;
  - `ReservationReadPolicy`: views, allowed actions and QR eligibility.
- Why: the web and Android clients call the same REST API, so a rule written once on the server is applied to both. The clients never decide capacity, the seven-day rule, the twelve-hour rule, overlap or status changes. They only display the server's result.
- Errors are thrown as `ConflictException` (409), `NotFoundException` (404), `ForbiddenException` (403) or `BadRequestException` (400). The shared `ExceptionMiddleware` turns them into `{status, message}`, so controllers need no try/catch.

## 2. Seven-day rule

- A booking may start at most 7 days after the server's current UTC time (`BusinessRules:MaxBookingDaysAhead = 7`, validated at startup). The boundary is inclusive.
- It is checked on create, on update when the booking moves to a new slot, and again on approval.
- A booking beyond the window gets 409 "Reservations cannot be scheduled more than 7 days ahead."
- Android explains it as "Bookings can be made only up to seven days ahead…" and the web shows the API message with guidance.
- The server's clock is used (`TimeProvider`), never the phone's or browser's clock.

## 3. Twelve-hour rule

- An update or cancellation is allowed only if the **existing** booking starts at least 12 hours from now (`MinChangeNoticeHours = 12`). Exactly 12 hours is allowed.
- The check always uses the old start time. Moving a booking later cannot be used to get around the rule.
- Creating a booking that starts within 12 hours is allowed. The rule applies only to changes.
- The API returns 409 "Reservations require at least 12 hours' notice to update / to cancel."
- The `allowedActions` DTO already carries the reason, so both clients disable Modify/Update and Cancel and show "The minimum notice period … has passed."

## 4. Duplicate and overlap prevention

- A Prosumer cannot hold two active (Pending or Approved) bookings for the same slot or overlapping times.
- Checking and then inserting is racy: two requests could both pass the check. So `ReservationSchedulingGuardService` first takes a MongoDB lease document for that Prosumer, which works across API instances. Only one create or update per Prosumer runs the overlap check at a time.
- A retried request with the same `Idempotency-Key` returns the original result instead of creating a second booking. The key is stored only as a hash, and a unique index `ux_reservations_creation_request_id_hash` protects it.

## 5. Capacity hold and release

- Each slot document keeps `AvailableCapacityKwh` and a list of embedded claims, one per reservation.
- **Hold** (on create): one conditional update subtracts the kWh and adds the claim. The filter requires that the available capacity is unchanged, that it is at least the requested amount, and that no claim exists yet for this reservation. If anything changed, nothing is written and the request is retried or refused.
- **Release** (on cancel or reject): removes exactly that claim and adds back its stored amount. It can happen only once.
- **Adjust** (on update): the same slot changes by the difference; a slot move releases the old claim and holds on the new slot.
- **Completed** (Member 4): the claim stays and becomes Consumed. Completed energy is never returned to the slot.
- A slot reaching 0 kWh becomes `FullyBooked`, and a release makes it `Available` again.

## 6. MongoDB transactions and why a replica set is needed

- A create touches several documents (the reservation, the slot claim and the station counter). They must succeed or fail together.
- `MongoTransactionRunner` runs this work in a MongoDB transaction. MongoDB supports multi-document transactions only on a replica set or sharded cluster, so production needs a replica set. The integration tests start a one-node replica set (`scripts/run-component3-tests.ps1`).
- On a standalone development server, MongoDB rejects transactions with error 20 "IllegalOperation". Only in that case does the runner switch to an explicit compensation workflow (idempotent claims and reconciliation). Network or unclear errors are never replayed automatically.

## 7. Race-condition protection

| Race | Protection | Proven by |
| --- | --- | --- |
| Two Prosumers want the last kWh | Slot claim compare-and-swap | Four simultaneous requests for the last 5 kWh: exactly one succeeded and three got 409 (local run) |
| Same Prosumer, two overlapping requests | Per-Prosumer scheduling lease | Lifecycle concurrency integration tests |
| Update against cancel, or approve against reject | Reservation `Version` compare-and-swap: the second writer's version no longer matches | `ReservationLifecycleSecurityConcurrencyTests` |
| Booking created while a station is being deactivated | Station `reservation_write_version`: create increments it while the station is active; deactivation only succeeds if the counter is unchanged and no active booking exists | Deterministic open-transaction test in `CrossMemberIntegrityIntegrationTests` (it fails without the fix) |
| Network retry of the same request | Hashed `Idempotency-Key` returns the first result | Replay tests |
| Complete against cancel or update | Member 4 completion requires the same version, Approved and Held | Local W9 run: stale receipt 409, second completion 409 |

## 8. Reservation statuses

- `Pending` → `Approved` (staff approve) → `Completed` (Member 4 QR completion).
- `Pending` → `Rejected` (staff, with a reason).
- `Pending` or `Approved` → `Cancelled` (owner or staff, with the twelve-hour rule).
- A material update of an `Approved` booking returns it to `Pending`, because the approved slot or amount changed.
- `Rejected`, `Cancelled` and `Completed` are final. Every change is checked against the legal transitions and recorded in the status history with the actor, the time and a new version.
- Capacity follows the status. The claim is held while the booking is Pending or Approved, released when it is Cancelled or Rejected, and consumed when it is Completed.

## 9. Authorization and ownership

- Identity comes only from the JWT (NIC and role). `[RequireActiveAccount]` re-reads the account from MongoDB on every reservation request, so a deactivated user is refused at once and both clients end the session.
- **Prosumer:** only their own bookings. Another Prosumer's booking returns **404**, not 403, so a Prosumer cannot even learn that it exists (tested locally: GET, PUT, cancel and QR all returned 404).
- **Grid Operator:** only reservations at their `AssignedStationId`. Another station returns 403, and a missing assignment returns 403.
- **Backoffice:** all reservations. It has no emergency override of the seven-day, twelve-hour or capacity rules.
- Staff can create, update and cancel on behalf of a Prosumer, but only for an active Prosumer, found through `GET /api/users/eligible-prosumers`.

## 10. Relationship with Member 4's QR

- Component 3 decides **whether** a QR may exist. `ReservationReadPolicy.IsQrUsable` requires Approved, capacity Held, and an end time still in the future. `allowedActions.canGetQr` and `qrEligible` come from the same rule.
- Member 4 issues, verifies and completes the QR, and re-checks the same conditions plus the reservation version.
- Every Component 3 change increases the version or leaves the Held state, so an old QR or verification receipt stops working automatically. This was shown locally: a stale receipt got 409 "The reservation changed…", and a QR issued before an update got 409 "no longer eligible".
- Completion is the only `Approved → Completed` transition and goes through the same legal-transition guard.

## 11. Why Member 2 and Member 4 issues are external dependencies

- Each member owns their own component. Component 3 uses Member 2's stations and slots and Member 4's dashboard, QR and deployment through agreed contracts (`docs/member-4/contracts.md`).
- Component 3 added the guards it needs at the shared boundaries, such as the station write counter and the slot-claim checks. It does not rewrite another member's screens or runbook.
- Recorded examples:
  - **Member 2:** the station detail lists slots beyond seven days (the API refuses them with a clear message), the station-delete wording, and schedule-edit checks.
  - **Member 4:** two Android lint errors, the camera scan not tested on a device, error-log noise, runbook gaps, and hosted IIS verification.
- Fixing them in Component 3 would blur ownership and could conflict with the owners' own changes. They are listed with owners in `docs/component-3/final-audit.md`.

## Challenges encountered (from the recorded work)

- **A race test that couldn't fail:** a random concurrency test passed even without the station fix. It was replaced by a deterministic test that holds a transaction open, and that test was confirmed to fail without the fix.
- **Concurrent lease upserts:** they could surface MongoDB duplicate-key error 11000. The lease now treats this as contention and retries.
- **Taps that did nothing on the emulator:** Android list rows looked tappable but never opened. A clickable card root and a selectable text view were taking the tap from the ListView. Found only by running the real app.
- **A confusing seven-day message:** the API sends the seven-day refusal as 409, and Android mapped every unknown 409 to "state changed". Found only in the real end-to-end run.
