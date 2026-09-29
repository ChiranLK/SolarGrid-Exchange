# Component 3 screenshot checklist

**No screenshot in this list has been captured for submission.** During the 2026-09-29 local verification, two emulator captures were taken only to read a toast message; they were deleted afterwards and must not be reused. Each item below must be a new, real capture from a running environment. Do not mock screens, edit values into an image, or reuse one image for two items.

## Capture rules

- Use synthetic accounts and data only (for example "Demo Prosumer A", "C3 Demo Station"). Never show a real NIC, email, password, JWT, connection string, verification receipt or a scannable live QR code.
- Crop out browser bookmarks, other tabs, notification trays and personal account names.
- Name each file `C3-<number>-<short-name>.png`. Keep the files outside the repository unless the team agrees on a sanitized evidence folder.
- Next to each file in the submission document, record the capture date, the environment (local emulator, physical device, local browser, or hosted) and the build commit (for example `4ed675e`).
- A local capture is evidence of the local environment only. Do not caption a local capture as IIS or hosted.

## Suggested setup

Follow "Test environment" in `docs/component-3/test-results.md`:

1. Run the API against an isolated database.
2. Seed a synthetic station with slots 2–3 days ahead, one slot 8 days ahead and one slot 4–10 hours ahead.
3. Create a Backoffice account, a Grid Operator assigned to the station, and two activated Prosumers.
4. For the Android emulator, run `adb emu geo fix <lng> <lat>` so Nearby stations lists the station.

## Android (Prosumer)

| # | Screen | State to show | Captured |
| --- | --- | --- | --- |
| 1 | Station details → Available slots | Live slot list with kWh available; one slot about to be tapped | No |
| 2 | Create booking form | Selected slot in the spinner, requested kWh entered, "Review booking" visible | No |
| 3 | Review booking | Seven-day notice text, Prosumer, station, date, time, capacity and requested kWh | No |
| 4 | Reservation Created summary | Status Pending, reference, station **name** (not an ID), time and kWh | No |
| 5 | My reservations: Pending approval | At least one Pending row | No |
| 6 | My reservations: Approved · upcoming | The approved booking row | No |
| 7 | Reservation details | Status, schedule, kWh, reference, allowed actions and status history | No |
| 8 | Modify reservation | Slot spinner and energy field populated | No |
| 9 | Reservation Updated summary | New time or kWh; the Approved → Pending notice if it applies | No |
| 10 | Cancel reservation confirmation | Reference, station, time, optional reason, CONFIRM CANCELLATION | No |
| 11 | Reservation Cancelled summary | Status Cancelled | No |
| 12 | Seven-day error | Toast "Bookings can be made only up to seven days ahead…" after submitting the 8-day slot. Capture while the toast is visible. | No |
| 13 | Twelve-hour rule | Details of a booking starting within 12 h: Modify and Cancel disabled, with "The minimum notice period … has passed." | No |
| 14 | Prosumer home after cancellation | Pending 0, Cancelled 1, and the booking in Recent booking history | No |

## Web (Backoffice / Grid Operator)

| # | Screen | State to show | Captured |
| --- | --- | --- | --- |
| 15 | `/reservations` list | Several reservations with reference, Prosumer, time, kWh and status | No |
| 16 | `/reservations` filters | Status filter and station filter applied (for example Pending at C3 Demo Station) | No |
| 17 | `/reservations/{id}` details | Booking information, allowed actions, audit history | No |
| 18 | Approve dialog | "Approve reservation?" confirmation before the decision | No |
| 19 | After approval | Status Approved, QR eligible | No |
| 20 | Reject form | Required reason entered before submitting | No |
| 21 | Staff create for a Prosumer | Eligible Prosumer search result selected, station, slot and kWh, then the review step | No |
| 22 | Reservation saved | "Reservation saved", RES-… reference, Pending | No |
| 23 | Web seven-day error | "Reservations cannot be scheduled more than 7 days ahead…" on the create page | No |
| 24 | Web twelve-hour rule | Details of a booking within 12 h: Update and Cancel disabled, with the reason text | No |

## Local end-to-end workflow

| # | Evidence | State to show | Captured |
| --- | --- | --- | --- |
| 25 | Android create → web list | Split or sequential capture: the Android "Reservation Created" summary, then the same reference Pending in the web list | No |
| 26 | Web approve → Android | Web detail Approved, then Android Approved · upcoming with "Show secure QR" (blur the QR if it is opened) | No |
| 27 | Station deactivation blocked | Web station page: "Station cannot be deactivated while it has pending or approved reservations." | No |

## Hosted IIS

Hosted IIS verification is **Not Verified**. Do not capture or caption any item as hosted until Member 4 (deployment owner) provides an authorised environment and the steps in `docs/component-3/test-results.md` ("Hosted IIS verification") have been run.
