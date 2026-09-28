# Member 4 screenshot checklist

No screenshot in this list has been captured yet. Each item must be a real capture from a configured environment. Do not reuse images between items, mock screens, or edit values into an image.

## Capture rules

- Use only synthetic test accounts, synthetic NIC-shaped values, and synthetic station/reservation data.
- Never capture a scannable live QR code, JWT, verification receipt, password, connection string, real NIC, or `Authorization` header. Blur or crop the QR image before saving, or capture it only after it has expired and cleared.
- Crop out browser bookmarks, other tabs, notification trays, and personal account names.
- Name each file `M4-<number>-<short-name>.png`. Store them outside the repository unless the team agrees on a sanitized evidence folder.
- Record the capture date, environment (local, emulator, device, or hosted) and build commit next to each file in the submission document.

## Owners

- **Member 4** captures every Member 4 web, Android, API and transaction-flow item below.
- **Group lead / deployment owner** captures the hosted IIS items (26-28) if Member 4 does not have access to the IIS host. Member 4 reviews those images for redaction before submission.

## Web client (Grid Operator)

| # | Screen | State to show | Owner | Captured |
| --- | --- | --- | --- | --- |
| 1 | `/operator/dashboard` | Populated: pending count/list, approved-future count, current count, status totals, active and recent transfers | Member 4 | No |
| 2 | `/operator/dashboard` | Empty-data state for a station with no reservations | Member 4 | No |
| 3 | `/operator/dashboard` | API unavailable / error state with Try again control | Member 4 | No |
| 4 | `/operator/history` | Unfiltered history, first page, with pagination controls | Member 4 | No |
| 5 | `/operator/history` | Filtered by status + date range + search text | Member 4 | No |
| 6 | `/operator/history` | Invalid date range message (from after to) | Member 4 | No |
| 7 | Operator route as Prosumer | Forbidden page, operator navigation hidden | Member 4 | No |
| 8 | `/operator/dashboard` | Mobile-width responsive layout (browser device toolbar, ~390 px) | Member 4 | No |

## Android client (Prosumer)

| # | Screen | State to show | Owner | Captured |
| --- | --- | --- | --- | --- |
| 9 | Prosumer home (`nav_prosumer_home`) | Populated dashboard: current/pending lists, approved-future and pending counts, status totals | Member 4 | No |
| 10 | Prosumer home | Empty or error/retry state | Member 4 | No |
| 11 | Booking history (`nav_booking_history`) | Search + status/station/date filters applied, paged results | Member 4 | No |
| 12 | Reservation detail | Approved reservation showing the QR action (server `canGetQr`) | Member 4 | No |
| 13 | Reservation QR (`nav_reservation_qr`) | Summary and countdown with the **QR image blurred/cropped** | Member 4 | No |
| 14 | Reservation QR | Expired state after the countdown clears the image | Member 4 | No |

## Android client (Grid Operator)

| # | Screen | State to show | Owner | Captured |
| --- | --- | --- | --- | --- |
| 15 | Operator home (`nav_operator_home`) | Server-authority notice and scanner entry | Member 4 | No |
| 16 | QR scanner (`nav_qr_operations`) | Camera permission explanation before the system prompt | Member 4 | No |
| 17 | QR scanner | Permanently denied state with app-settings link | Member 4 | No |
| 18 | Verification (`nav_transaction_verification`) | Successful verification: reference, `PRO-` alias, station, schedule, kWh, receipt expiry | Member 4 | No |
| 19 | Verification | Rejected scan (expired, already used, or wrong station) | Member 4 | No |
| 20 | Completion confirmation dialog | Explicit confirmation before completion | Member 4 | No |
| 21 | Completion (`nav_transaction_completion`) | Server completion summary with Completed status and timestamp | Member 4 | No |

## API, Swagger/OpenAPI and health

| # | Evidence | State to show | Owner | Captured |
| --- | --- | --- | --- | --- |
| 22 | OpenAPI document / API explorer | The five Member 4 operations (dashboard, history, QR issue, verify, complete) with bearer security | Member 4 | No |
| 23 | OpenAPI schema | Redacted example for the verification or completion response | Member 4 | No |
| 24 | `GET /health` | 200 response with healthy `api` and `database` components and correlation ID | Member 4 | No |
| 25 | Test run output | Terminal showing `dotnet test` 73/73 API and 69/69 service results, plus `npm run check` | Member 4 | No |

## Hosted IIS deployment

| # | Evidence | State to show | Owner | Captured |
| --- | --- | --- | --- | --- |
| 26 | IIS Manager | `SolarGridApi` site and dedicated app pool running (no secrets in the view) | Group lead / deployment owner | No |
| 27 | Browser | Hosted `https://<api-host>/health` returning 200 over a trusted certificate | Group lead / deployment owner | No |
| 28 | Web client | Hosted web build loading the operator dashboard against the hosted API | Group lead / deployment owner | No |

## Successful transaction flow (one continuous run)

Capture these from a single sanitized reservation so the reference matches across images.

| # | Step | Owner | Captured |
| --- | --- | --- | --- |
| 29 | Prosumer dashboard shows the reservation as approved-future/current | Member 4 | No |
| 30 | Prosumer QR screen open (QR blurred) | Member 4 | No |
| 31 | Operator verification success for the same reference | Member 4 | No |
| 32 | Operator completion summary for the same reference | Member 4 | No |
| 33 | Refreshed web history filtered to Completed showing the same reference | Member 4 | No |
| 34 | Repeat scan of the same token rejected (replay prevention) | Member 4 | No |
