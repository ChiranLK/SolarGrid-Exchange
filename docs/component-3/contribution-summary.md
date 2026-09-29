# Component 3 contribution summary (Member 3)

**Member:** Alahakoon PB. **IT number:** IT23405240. Commits are recorded under the git identity "Pramodya Alahakoon".

**Status:** the Component 3 implementation and the ten local workflows are complete. Hosted IIS verification is Not Verified. The pull request is **security-blocked** until the database owner confirms rotation of the credential in historical commit `46f06d9`. The submission documentation is **incomplete** until the AI-use disclosure below is completed.

**Component:** Energy Reservation Workflow (Component 3).

**Branch:** `feature/component3-integration`, based on `develop` at `dfe91a9`. The original implementation branch was `feature/reservation-workflow`.

## Member 3-owned implementation

| Commit | Date | Work |
| --- | --- | --- |
| `3a0c838` | 2026-09-23 | Reservation lifecycle rules and API contract (`docs/component-3/reservation-contract.md`) |
| `89fe8b5` | 2026-09-23 | Reservation entity, status and capacity-state model, request/response/query DTOs, MongoDB mapping and indexes |
| `76aebc0` | 2026-09-23 | `ReservationCapacityService` (hold, adjust, release with exact-claim compare-and-swap) and `MongoTransactionRunner` (transactions with an explicit standalone fallback) |
| `74e4a08` | 2026-09-23 | Capacity-safe creation (Prosumer and staff), seven-day rule, duplicate and overlap guard (`ReservationSchedulingGuardService`), hashed idempotency keys |
| `bccf4fc` | 2026-09-24 | Update and reschedule with the twelve-hour rule, version checks, capacity moves and the Approved → Pending reset |
| `857912a` | 2026-09-24 | Idempotent cancellation with exact-once capacity release |
| `eb4c31b` | 2026-09-24 | Approval and rejection transitions for Backoffice and the assigned Grid Operator |
| `a6e5bb9` | 2026-09-24 | Authorized reservation queries, views and filters (`ReservationReadPolicy`) |
| `b0d3552` | 2026-09-24 | MongoDB integration tests for lifecycle rules, security and concurrency |
| `08fa347` | 2026-09-24 | Web reservation list with filters, and details |
| `ad0d619` | 2026-09-24 | Web staff creation for a Prosumer, with eligible-Prosumer search (API endpoint and page) |
| `0deb9c3` | 2026-09-24 | Web update, cancel, approve and reject actions |
| `2cb3a94` | 2026-09-25 | Android reservation API client, navigation and local state |
| `e707319` | 2026-09-25 | Android available-slot selection and booking review |
| `95e4580` | 2026-09-25 | Android reservation creation and creation summary |
| `73eb818` | 2026-09-25 | Android current and pending lists, and reservation details |
| `175d523` | 2026-09-25 | Android update, cancellation and action summaries |

## Cross-member integration changes

| Commit | Change | Other member affected |
| --- | --- | --- |
| `1792670` | Shared native Android foundation (Java/XML, SQLite session store) used by every member | All (shared foundation) |
| `a9db5bb` | `[RequireActiveAccount]` on reservation endpoints and the canonical "not active" message; Android and web end the session on account-status 403 | Member 1 |
| `a9db5bb` | Station `reservation_write_version`, which closes the create-against-deactivate race; slot delete and time changes refused while capacity claims exist; capacity CAS also compares the availability status | Member 2 |
| `a9db5bb` | QR allowed actions and `qrEligible` aligned with Member 4's issuance rule (`IsQrUsable`); Android booking history refreshes on return | Member 4 |
| `c7558f3` | Tappable Android station rows (`item_station.xml`) and reservation rows (`item_reservation.xml`, also used by Member 4's dashboard and history lists) | Member 2 and Member 4 screens |
| `c7558f3` | Clear Android seven-day messages; station name, station address and Prosumer name in every mutation response | — (Component 3) |

## Tests

| Suite | Latest result (2026-09-29) |
| --- | --- |
| Backend unit (`SolarMicrogrid.Tests`) | 216 / 216 passed |
| MongoDB 8 replica-set integration (`scripts/run-component3-tests.ps1`) | 92 / 92 passed |
| Web lint, vitest, typecheck, production build | 0 problems, 133 / 133, pass, pass |
| Android `assembleDebug`, `testDebugUnitTest` | pass, 80 / 80 |
| Android standalone JVM tests (`scripts/run-member4-android-unit-tests.ps1`) | 17 / 17 |
| Android `lintDebug` | 2 errors, both existing Member 4 findings (not fixed) |

Component 3's own test files:

- Integration: `ReservationCreationIntegrationTests`, `ReservationUpdateIntegrationTests`, `ReservationLifecycleSecurityConcurrencyTests`, `ReservationReadPolicyTests`, `ReservationQrActionPolicyTests`, `EligibleProsumerSearchIntegrationTests`, `CrossMemberIntegrityIntegrationTests`.
- Unit: `CapacityStatusPreservationTests`.
- Web: `reservationActions`, `reservationCreation`, `reservationFormatters`, `reservationNavigation`.
- Android: `ReservationCreationReconcilerTest`, `ReservationMutationReconcilerTest`, `BookingHorizonMessageTest`, `ReservationFormattersTest`, `ReservationModelsTest`, `SharedReservationContractTest`.

## Local runtime verification

All ten end-to-end workflows passed on 2026-09-29, using the real API, the web client in a browser and the Android app on an emulator, all running locally with synthetic data. The details are in `docs/component-3/test-results.md`. The run found four Component 3 defects, fixed in `c7558f3`:

1. Station rows could not be tapped.
2. Reservation rows could not be tapped.
3. The seven-day refusal showed a generic message on Android.
4. Mutation responses had no station or Prosumer names.

## Documentation

- `docs/component-3/reservation-contract.md`: the authoritative contract.
- `docs/component-3/progress.md`: the progress log.
- `docs/component-3/test-results.md`: end-to-end results and the IIS review.
- `docs/component-3/final-audit.md`, `screenshot-checklist.md`, `viva-notes.md`, `video-script.md`: submission support.
- The Component 3 section of `docs/member-4/contracts.md`.

## External dependencies

These are recorded with owners in `docs/component-3/final-audit.md`. In short:

- **Member 2:** station-delete wording; slots beyond seven days shown in the station detail; schedule edits not checked against reservations; `SlotService` races and clock use; the "all slots" Android list cannot start a booking.
- **Member 4:**
  - two Android lint errors;
  - camera scan not tested on a device;
  - cancelled requests logged as errors;
  - runbook gaps: web hosting, bootstrap keys, business-rule keys, Component 3 smoke tests;
  - Member 4 Android screens do not end the session on account-status 403;
  - review of the booking-history failed-refresh behaviour (Partial: the silent refresh added by Component 3 replaces a visible list with an error);
  - hosted IIS verification.
- **Member 1:**
  - rotation and security-note correction for the credential in historical commit `46f06d9` (rotation not confirmed);
  - agreement with Member 3 on the web Prosumer/staff route decision (Partial).
- **Team decisions (Blocked):** reservations on a slot set Unavailable; kWh precision; Pending after its start; the check-in window.

**Hosted IIS verification: Not Verified.**

## Genuine challenges

- **A race test that proved nothing.** The first station-deactivation race test was random and passed even without the fix. It was replaced by a deterministic test that holds a transaction open, and that test was confirmed to fail without the fix.
- **Concurrent lease creation.** It could surface MongoDB duplicate-key error 11000. The lease now treats that as contention and retries.
- **Standalone MongoDB and transactions.** A standalone MongoDB rejects transactions, so the runner falls back to an explicit compensation workflow only for that definite error, and never for unclear network failures.
- **Defects visible only in the real app.** Two Android list layouts made rows focusable, which silently disabled taps. The seven-day refusal arrived as 409 and was shown as a generic conflict. Both were found only by driving the real emulator.

## AI-use disclosure (COMPLETION REQUIRED)

> **Not yet completed: the submission documentation is incomplete until this section is filled in.**
>
> Member 3 must complete this truthfully, following the university's policy on AI assistance:
> - which tools were used;
> - for which parts (for example code, tests, documentation, debugging or verification);
> - how the output was reviewed and verified.
>
> Do not write "No AI used" if AI-assisted work occurred. Do not submit with this section blank.

## Ready-to-paste README / report block

The shared `README.md` is maintained by Member 4 and has only a placeholder table row for Member 3 (`Member 3 <name / ID>`). It has no dedicated Component 3 section, so it has not been edited. Paste the block below into the report, or give it to Member 4 for the README:

```markdown
### Member 3 contribution (Component 3: Energy Reservation Workflow)

**Member:** Alahakoon PB (IT23405240)
**Branch:** `feature/component3-integration`. Key commits: `74e4a08` creation, `bccf4fc` update, `857912a` cancellation, `eb4c31b` approve/reject, `a6e5bb9` queries, `b0d3552` tests, `08fa347`/`ad0d619`/`0deb9c3` web, `2cb3a94`–`175d523` Android, `a9db5bb` cross-member integration, `c7558f3` end-to-end fixes, `4ed675e` verification record.

- **API:** reservation create (Prosumer and staff for a Prosumer), update/reschedule, cancel, approve, reject and authorized queries. Business rules are all on the server:
  - seven-day booking window, and a twelve-hour notice for updates and cancellations;
  - duplicate and overlap prevention;
  - atomic capacity hold, adjust and release on the slot;
  - legal status transitions and optimistic versions;
  - hashed idempotency keys;
  - active-account checks, Prosumer ownership (404 for others) and Grid Operator station scope;
  - QR eligibility shared with Member 4.
- **Web (staff):** reservation list with view, status, station and search filters; details; approve and reject; update and cancel on behalf of a Prosumer; staff creation with eligible-Prosumer search.
- **Android (Prosumer):** slot selection from station details; create with review and summary; Pending, Approved-upcoming, current and all lists; details; modify and cancel with summaries; clear seven-day and twelve-hour messages.
- **Tests:**
  - backend unit 216/216;
  - MongoDB replica-set integration 92/92;
  - web 133/133;
  - Android 80/80 plus 17/17 standalone;
  - Android lint has only 2 existing Member 4 errors.
- **Local runtime verification:** all ten end-to-end workflows passed with the real API, web and Android clients (details: `docs/component-3/test-results.md`).
- **External dependencies:** Member 2 station/slot items, Member 4 lint/scanner/deployment items, and four team decisions (see `docs/component-3/final-audit.md`).
- **Hosted IIS verification:** Not Verified.
- **Report items still required:** real screenshots from `docs/component-3/screenshot-checklist.md` (none captured yet) and the AI-use disclosure.
```
