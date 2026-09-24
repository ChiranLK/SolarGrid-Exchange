# SolarGrid Exchange Android

Shared pure-native Android foundation for all SolarGrid Exchange mobile features. It is written in Java with AndroidX and XML layouts. It communicates only with the central ASP.NET Core API and uses one app-private SQLite database for authenticated session identity.

## Selected Android toolchain

- Application ID and namespace: `com.solargrid.exchange` (no earlier Android convention existed)
- Minimum SDK: 24 (Android 7.0)
- Compile SDK: 36
- Target SDK: 36
- Android Gradle Plugin: 8.13.2
- Gradle wrapper: 8.13
- Java source/target compatibility: 17

SDK 24, 34, 35, and 36 and build tools through 36 were installed when this foundation was created. Android Studio may use any supported JDK for Gradle, while application bytecode targets Java 17.

## Architecture

- `StartupActivity` validates or clears the locally persisted session before routing.
- `LoginActivity` and `LoginViewModel` call the real `POST /api/auth/login` endpoint.
- `MainActivity` hosts one AndroidX Navigation graph and a responsive navigation drawer.
- Exact API roles (`Prosumer`, `GridOperator`, and `Backoffice`) control the initial home destination and visible navigation.
- Android `ViewModel` and `LiveData` preserve request/UI state through activity and fragment recreation.
- `AppContainer` provides dependencies manually; no unrelated dependency-injection framework is used.
- `ApiClient` uses `HttpURLConnection`, bounded timeouts, bearer-token attachment, main-thread callbacks, and shared 400/401/403/404/409/server/network error mapping.
- Feature repositories separate authentication, users/prosumers, stations/slots, reservations, and operator transactions.

The mobile client does not contain reservation, capacity, approval, cutoff, or authorization business rules. Those remain in the C# API.

## SQLite session database

`SessionDatabaseHelper` extends `SQLiteOpenHelper` and owns `solargrid_local.db`, schema version 1. It contains one `authenticated_session` row with the API token and identity summary: NIC, full name, email when restored from `/api/auth/me`, exact role, status, and local update timestamp.

- Passwords are never written to SQLite, preferences, logs, or source.
- There is one session database and one session table; feature teams must extend it through explicit forward-only migrations instead of creating competing user/session stores.
- `SessionStore` provides synchronized read, replace, and clear operations.
- The database is app-private and is not a booking database.
- Reservation success, approval, capacity, and history must always come from the API.
- This foundation does not queue offline bookings and never represents an offline action as confirmed.

Inspect the database from Android Studio using **View > Tool Windows > App Inspection > Database Inspector** while a debuggable app process is running.

## Confirmed API integration

The implementation was matched to the repository's controllers and DTOs:

| Endpoint | Android usage |
| --- | --- |
| `POST /api/auth/login` | Login and local session creation |
| `GET /api/auth/me` | Startup token validation and email/role refresh |
| `GET /api/stations?isActive=true&page=1&pageSize=100` | Active station list |
| `GET /api/stations/{stationId}` | Station details |
| `GET /api/stations/{stationId}/slots/available` | Live available-slot display |
| `GET /api/reservations?view=...` | Role-scoped current, pending, approved-future, all, and history lists |
| `GET /api/reservations/{reservationId}` | Authoritative details, status history, and allowed actions |
| `POST /api/reservations` | Prosumer booking creation with an idempotency key |
| `PUT /api/reservations/{reservationId}` | Versioned slot/energy modification with an idempotency key |
| `POST /api/reservations/{reservationId}/cancel` | Versioned cancellation with an optional reason and idempotency key |

`GET /api/stations/nearby` also exists, but acquiring runtime location and rendering Member 2's map are deliberately left to that feature integration. The current nearby-stations destination displays API-backed active stations and identifies slot availability as a live, unconfirmed API result. No availability is cached in SQLite.

Component 3 adds API-backed booking creation from Member 2's available-slot screen, including a fresh station-slot query, single selection, required kWh input, seven-day guidance, and a signed-in Prosumer review screen before submission. Back navigation preserves the valid draft, while the central API remains authoritative for availability and capacity at confirmation. Creation sends only slot and quantity because ownership comes from authenticated claims. One idempotency key is retained for each logical request; after a network interruption, the client reconciles against a pre-submit reservation baseline before reporting an uncertain outcome. Successful creation opens a dedicated server-response summary and refreshes the Pending list endpoint used by Android and web. The app also provides filtered current/pending reservation lists, history, details, modification, cancellation, and update/cancel response summaries. Failed or offline requests are never queued or represented as confirmed. QR verification/operator transactions remain placeholders because the corresponding Component 4 API endpoints are not implemented. No fake production data is returned.

## Open in Android Studio

1. Open the `SolarGridAndroid` folder as the project.
2. Confirm Android SDK platform 36 and build tools are installed in SDK Manager.
3. Let Android Studio use the checked-in Gradle wrapper and complete Gradle sync.
4. Select the `app` run configuration and a device running API 24 or newer.

## API URL configuration

The build-time `SOLARGRID_API_BASE_URL` Gradle property or environment variable overrides the default. It must include the `/api/` suffix.

```powershell
$env:SOLARGRID_API_BASE_URL = 'http://192.168.1.25:5076/api/'
.\gradlew.bat assembleDebug
```

The debug default is `http://10.0.2.2:5076/api/`, matching the API's HTTP launch profile through the Android emulator host alias. `10.0.2.2` is only appropriate for an emulator connecting to an API running on the same development computer.

- Physical device: use the computer's reachable LAN address, run the API on an appropriate network binding, and allow the port through the firewall.
- Production: use HTTPS and a trusted certificate. Cleartext traffic is disabled in the main manifest and enabled only by the debug manifest overlay for local development.
- Never commit tokens, credentials, signing keys, `local.properties`, or secret configuration.

## Build and test

From `SolarGridAndroid`:

```powershell
.\gradlew.bat clean testDebugUnitTest assembleDebug
```

The APK is written beneath `app/build/outputs/apk/debug/`. An emulator or physical device is required to verify activity navigation, Database Inspector, and live API connectivity; the Gradle build and local JVM tests do not require a device.
