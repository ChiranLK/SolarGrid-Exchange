# Group video script (maximum 5:00)

Total budget is five minutes. Each member keeps to their slot so the full video stays under the limit. Timings for Members 1-3 are placeholders for the group to adjust; the Member 4 segment is written out in full.

Recording rules for every segment:

- Use synthetic accounts and data only. Never show a JWT, live scannable QR, verification receipt, password, connection string, or real NIC on screen.
- Keep the QR screen on camera only long enough for the scan, or record the Prosumer phone from an angle where the code is not readable in the final video.
- Hide browser bookmarks, notifications and personal accounts.

## Running order

| Time | Segment | Presenter |
| --- | --- | --- |
| 0:00-0:20 | Introduction: system purpose, architecture (C# API + MongoDB, React web, native Android), roles | Group lead |
| 0:20-1:20 | Member 1: login, JWT roles, user/prosumer management, Android SQLite session | Member 1 |
| 1:20-2:20 | Member 2: stations, slots, capacity, station/map data | Member 2 |
| 2:20-3:30 | Member 3: create, update, cancel, approve/reject reservations and scheduling rules | Member 3 |
| 3:30-4:45 | Member 4: dashboard -> QR -> scan -> verify -> complete -> refreshed history, plus IIS/API evidence | Member 4 |
| 4:45-5:00 | Close: repository link and summary | Group lead |

## Member 4 segment (3:30-4:45, 75 seconds)

Preparation before recording:

- One synthetic reservation already approved by Member 3's flow, at the operator's assigned station, scheduled so it is current or approved-future.
- Prosumer signed in on one Android device, Grid Operator signed in on a second Android device with camera permission already granted.
- Web client open as the same Grid Operator on `/operator/dashboard`.
- A browser tab with the API `/health` response, and a tab with the OpenAPI document showing the Member 4 operations.

| Time | On screen | Narration (suggested) |
| --- | --- | --- |
| 3:30-3:40 | Prosumer Android home dashboard with counts and the approved reservation | "The Prosumer dashboard comes straight from the API: current, pending and approved-future counts are calculated on the server for this user only." |
| 3:40-3:50 | Open reservation detail, tap the QR action, QR screen with countdown | "The QR is only offered when the server says the reservation is eligible. It holds a short-lived random token with no NIC, JWT or reservation data, and it expires in five minutes." |
| 3:50-4:05 | Operator device scans the QR; verification screen shows reference, `PRO-` alias, station, kWh | "The operator's app sends the token to the API. The server checks the operator's assigned station, the reservation status and version, and expiry, then returns a one-time verification receipt." |
| 4:05-4:15 | Operator taps Complete, confirms the dialog, completion summary shows Completed | "Completion is an explicit, confirmed step. The reservation moves from Approved to Completed in one atomic MongoDB transaction, so it can only happen once." |
| 4:15-4:25 | Operator scans the same QR again, rejection message | "Scanning the same code again is rejected: the token has already been used." |
| 4:25-4:35 | Web operator booking history, filter Completed, same reference visible; dashboard counts updated | "The web dashboard and booking history refresh from the server and show the transfer as Completed." |
| 4:35-4:45 | `/health` 200 response, then OpenAPI Member 4 operations; if available, IIS Manager site running | "The API exposes a database health check and documented OpenAPI endpoints, and is published for IIS with secrets injected from the environment, never from source control." |

If the hosted IIS deployment is not available at recording time, show the local Release API `/health` and OpenAPI only, and say "running locally" rather than claiming a hosted deployment.
