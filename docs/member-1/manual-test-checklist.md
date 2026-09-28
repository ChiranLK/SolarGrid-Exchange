# Member 1 manual runtime checklist

Use synthetic test data only (for example `2099999999xx` NICs and `@example.com` emails). Tick each item and
note the date, device/browser and result. Record screenshots for the report only after removing personal data.

## 0. Environment

- [ ] Local MongoDB and secrets set up as in `docs/member-1/local-development.md`.
- [ ] API running: `dotnet run --project SolarMicrogrid.API --launch-profile http`; `http://localhost:5076/health`
      shows `api` and `database` as `Healthy`.
- [ ] First Backoffice created with `docs/member-1/backoffice-bootstrap.md`, then `BootstrapBackoffice__Enabled`
      turned off and the bootstrap password removed.
- [ ] Web, in `SolarMicrogrid.Web` (PowerShell): `$env:VITE_API_PROXY_TARGET = 'http://localhost:5076'; npm run dev`,
      then open `http://localhost:5173`.
- [ ] Android: Android Studio → run `app` (debug) on an emulator; the debug build calls `http://10.0.2.2:5076/api/`.

## 1. Android registration and pending flow

- [ ] Login → "Create a Prosumer account". Submit empty: every field shows an error; nothing is sent.
- [ ] Enter a mismatched confirm password: error on the confirm field only.
- [ ] Register valid synthetic data → the pending-activation screen shows the registered email.
- [ ] Register the same NIC again → the NIC field shows "A user with this NIC already exists."
- [ ] Sign in with the new account → pending-activation screen (not an error).
- [ ] Rotate the registration screen mid-entry → values kept, password fields cleared, no duplicate request.

## 2. Web activation

- [ ] Sign in as Backoffice → lands on **Users** (`/users`).
- [ ] **Pending activation** tab shows the new Prosumer; Activate → confirmation dialog → success message; tab count drops.
- [ ] Activating the same account again from another tab shows the API's 409 message.

## 3. Profile update (Android)

- [ ] Sign in as the activated Prosumer → Prosumer home. Open **Profile & account**.
- [ ] NIC and status are read-only; name, email, phone and address load from the API.
- [ ] Change the name and phone → "Your profile has been updated."
- [ ] Change the email → "Use your new email address the next time you sign in"; sign out and sign in with the new email.
- [ ] Use an email that belongs to another account → error on the email field.
- [ ] Edit a field, rotate the device → the unsaved edit is kept.
- [ ] Turn on airplane mode and reopen Profile → offline state with **Try again**.

## 4. Deactivation request and approval

- [ ] Profile → **Request account deactivation** → dialog explains Backoffice approval → Cancel sends nothing.
- [ ] Confirm → success message; state shows "Requested … awaiting Backoffice review"; button disabled.
- [ ] Web → **Deactivation requests** lists the Prosumer with the request time → Approve → confirmation → success.
- [ ] Android: reopen the app or Profile → signed out with "Your account is no longer active…".
- [ ] Web → Users → filter Deactivated → **Reactivate** → the Prosumer can sign in again.

## 5. Role routing

- [ ] Web: Grid Operator lands on the operator dashboard; `/users`, `/users/pending`, `/users/deactivation-requests`
      and `/users/new` typed directly show Access denied; no Users link in the menu.
- [ ] Web: Prosumer lands on the "Use the SolarGrid mobile app" page; admin URLs are blocked.
- [ ] Web: signed-out visit to `/users/pending` → login → after sign-in returns to `/users/pending`.
- [ ] Android: Grid Operator → operator home (unchanged). Backoffice → "Use the web application" screen and no session kept.
- [ ] Web: create a Grid Operator with a station chosen from active stations; change the station from the user list.

## 6. SQLite session behaviour (Android)

- [ ] Sign in, close and reopen the app → goes straight to home (session restored via `/api/auth/me`).
- [ ] Sign out → reopen → login screen.
- [ ] Deactivate the signed-in user on the web, then reopen the app → login screen with the account notice.
- [ ] Automated: `./gradlew connectedDebugAndroidTest` on an emulator (3 tests).

## 7. Accessibility and layout

- [ ] Web: complete create-staff, activate and deactivate using only the keyboard (Tab, Enter, Escape closes dialogs).
- [ ] Web: screen reader announces success and error messages and dialog titles.
- [ ] Web: phone width (375 px) — tables scroll horizontally inside their card; forms stack to one column.
- [ ] Android: TalkBack reads field labels, errors and the dialog; buttons are at least 48 dp.
- [ ] Android: small phone and large phone emulators; landscape orientation for registration and profile.

## Results — 2026-09-29

Environment: Windows 11; API `http` profile on `localhost:5076` with user secrets; MongoDB 8.0 replica set in
Docker (local); Chrome with the Vite dev server; Android debug build on the Pixel_9 emulator (API 36).
Synthetic accounts only. Evidence: tester observation plus MongoDB status fields of the synthetic accounts.

| Section | Result | Notes |
| --- | --- | --- |
| 0. Environment | Passed | Bootstrap created one Backoffice, then disabled; API restarted without bootstrap settings |
| 1. Android registration and pending flow | Passed (partial) | Registration → Awaiting activation. Pending-login repeat, empty-form and rotation checks not confirmed |
| 2. Web activation | Passed | Activated from Pending activation; server status Active |
| 3. Profile update (Android) | Passed (partial) | Name, phone and email saved and confirmed on the server; old email refused, new email accepted. Rotation and offline checks not confirmed. Required the edge-to-edge menu fix first |
| 4. Deactivation request and approval | Passed (partial) | Request and approval confirmed on the server; sign-out observed after app refresh. Web reactivation not performed |
| 5. Role routing | Not Verified | Skipped (no station or Grid Operator created; Backoffice on Android not tried) |
| 6. SQLite session behaviour | Passed (partial) | Session restore and post-deactivation sign-out observed; instrumented tests 3/3 |
| 7. Accessibility and layout | Passed (partial) | Web dialog keyboard open/Escape and 375 px layouts passed; keyboard confirm and TalkBack not verified |

Still to do before claiming full completion: section 5, web reactivation, Android rotation and offline checks,
keyboard confirm and TalkBack.
