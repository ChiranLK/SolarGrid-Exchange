# Component 3 video segment (group video, maximum 5:00)

The group running order in `docs/member-4/video-script.md` gives Member 3 the slot **2:20–3:30 (70 seconds)**. This segment covers create → pending → approve → modify/cancel → summaries, using only screens and API results that exist and were verified locally on 2026-09-29.

The video is recorded against a **local** environment. It does not show or claim an IIS deployment. Hosted IIS verification is Not Verified.

## Recording rules

- Use synthetic accounts and data only (Demo Prosumer A, C3 Demo Station, a synthetic Backoffice or Grid Operator). Never show a password, JWT, connection string, real NIC or a scannable QR code.
- Hide browser bookmarks, notifications and personal accounts. Crop the emulator status bar if it shows personal notifications.
- Do not speed up or edit results into the recording. If a step fails, re-record it.

## Preparation (before recording)

1. Start the API against an isolated demo database (bootstrap disabled after the first Backoffice account), the web client, and the Android emulator with the debug build.
2. Seed one active station with:
   - an all-day schedule;
   - one 10 kWh slot 2–3 days ahead;
   - a second slot 2–3 days ahead to move to;
   - one slot 8 days ahead.
3. Sign in as Prosumer A on Android and open Nearby stations (set the emulator location with `adb emu geo fix` so the station is listed).
4. Sign in as the Grid Operator (assigned to the station) or Backoffice on the web client, and open `/reservations`.

## Segment (2:20–3:30)

| Time | On screen | Narration (suggested) |
| --- | --- | --- |
| 2:20–2:30 | Android: Nearby stations → C3 Demo Station → tap a slot → enter 4 kWh → Review booking | "Component 3 is the reservation workflow. The Prosumer picks a live slot and a quantity. The review screen reminds them bookings are limited to seven days ahead." |
| 2:30–2:38 | Submit → "Reservation Created" summary: Pending, station name, time, 4.00 kWh | "The server checked the seven-day window, overlap and capacity, and held 4 kWh in one atomic update. The booking starts as Pending." |
| 2:38–2:48 | Web `/reservations`: the same reference Pending; open it, click Approve, confirm the dialog → Approved, QR eligible | "Staff see the same booking on the web. The Grid Operator can only act on their own station. Approving sends the version they saw, so a stale screen cannot overwrite a newer change." |
| 2:48–2:56 | Android: My reservations → Approved · upcoming → details with Modify, Cancel reservation, Show secure QR | "The phone refreshes from the API, so the Prosumer sees Approved and the QR entry point from Member 4, which is offered only while the server says the booking is eligible." |
| 2:56–3:08 | Modify → change to the second slot or a new quantity → "Reservation Updated" summary, with the Approved → Pending notice | "Changing an approved booking moves the capacity and returns it to Pending for a new decision. This is only allowed at least twelve hours before the original start." |
| 3:08–3:18 | Details → Cancel reservation → confirmation dialog → "Reservation Cancelled" summary; Prosumer home shows Cancelled 1 | "Cancelling releases the held energy exactly once, and the dashboard counts update from the server." |
| 3:18–3:30 | Android: the 8-day slot → submit → toast "Bookings can be made only up to seven days ahead…"; optionally a booking within 12 hours with Modify and Cancel disabled and the reason shown | "The rules are enforced on the server and explained in the app: a slot beyond seven days is refused, and a change within twelve hours is blocked with the reason." |

## Optional backup evidence (only if time is saved elsewhere)

- The concurrency result from `docs/component-3/test-results.md`: four simultaneous requests for the last 5 kWh, one success. Show this only as the recorded table, not as a new claim.
- The integration test run output: `scripts/run-component3-tests.ps1` → 92/92 passed.
