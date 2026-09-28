# SolarGrid-Exchange
SolarGrid Exchange: A Client–Server Smart Solar Microgrid Energy Trading and Reservation System

Repository: `<repository-url>` (replace with the submission repository link)

## Architecture

```text
 React web client (Bootstrap 5)      Native Android client (Java, XML, SQLite session)
            \                                   /
             \          HTTPS + JWT bearer     /
              v                               v
        ASP.NET Core Web API (.NET 10, controllers -> FAT services)
                              |
                              v
                MongoDB (replica set for transactions)
```

- **`SolarMicrogrid.API/`**: central ASP.NET Core Web API.
  - Thin controllers call scoped services that hold all business rules (FAT service pattern).
  - MongoDB through one `MongoDbContext`; indexes are created at startup.
  - JWT bearer authentication; shared `{ "status", "message" }` error responses.
- **`SolarMicrogrid.Web/`**: React 19 + TypeScript + Vite + Bootstrap 5.
  - A UI-only API client: one Fetch wrapper (`src/api/apiClient.ts`), no business rules and no database access.
- **`SolarGridAndroid/`**: pure-native Android (Java 17, AndroidX, XML layouts, Fragments/ViewModels).
  - One `HttpURLConnection` API client.
  - One app-private SQLite database for the signed-in session only; reservations are never cached.
- **Tests**: `SolarMicrogrid.API.Tests/` (xUnit, real MongoDB replica set) and `SolarMicrogrid.Tests/` (xUnit/Moq service tests).
- **`docs/`**: component contracts, progress logs, the IIS runbook and Member 4 audit material.

The API is the only authority for identity, roles, time, reservation state, counts, QR eligibility and completion. Clients display server responses.

## Roles

| Role | Main capabilities |
| --- | --- |
| `Backoffice` | User and prosumer management, station/slot management, reservation approval/rejection, global dashboard/history |
| `GridOperator` | Station-scoped dashboard and history, scan and verify Prosumer QR codes, complete verified energy transfers |
| `Prosumer` | Book/modify/cancel reservations, personal dashboard and history, display the QR for an approved reservation |

Role names are exact and case-sensitive in the JWT and in both clients.

## Prerequisites

- .NET SDK 10
- Node.js 22.12+ and npm
- MongoDB running as a replica set (needed for multi-document transactions). Docker Desktop is enough for local tests.
- Android Studio with Android SDK platform 36 and build tools, plus an emulator or device running API 24+
- Windows Server + IIS + .NET 10 Hosting Bundle for deployment

## Configuration

Tracked configuration contains **no secrets**. `JwtSettings:Key` and `MongoSettings:ConnectionString` are intentionally empty.

For local development, use .NET user secrets (the API project has a `UserSecretsId`):

```powershell
dotnet user-secrets --project SolarMicrogrid.API set "MongoSettings:ConnectionString" "<local-replica-set-connection-string>"
dotnet user-secrets --project SolarMicrogrid.API set "JwtSettings:Key" "<random-value-at-least-32-characters>"
```

For IIS, set them as environment variables (`MongoSettings__ConnectionString`, `JwtSettings__Key`). See [docs/deployment/iis-deployment.md](docs/deployment/iis-deployment.md) for the full list.

Never commit connection strings, JWT keys, tokens, QR values or real NIC data.

## Running locally

1. **API**:

   ```powershell
   dotnet restore SolarMicrogrid.slnx
   dotnet run --project SolarMicrogrid.API --launch-profile https
   ```

   - The API listens on `https://localhost:7168` and `http://localhost:5076`.
   - OpenAPI is enabled in Development at `/openapi/v1.json`.
   - Health is at `/health`.

2. **Web**:

   ```powershell
   cd SolarMicrogrid.Web
   npm ci
   npm run dev
   ```

   Open `http://localhost:5173`. Vite proxies `/api` to the API (see `.env.example`).

3. **Android**:
   1. Open `SolarGridAndroid` in Android Studio and let Gradle sync.
   2. Run the `app` configuration.
   3. The debug build calls `http://10.0.2.2:5076/api/`, the emulator's alias for the development machine. For a physical device, override the URL:

   ```powershell
   $env:SOLARGRID_API_BASE_URL = 'http://<lan-ip>:5076/api/'
   .\gradlew.bat assembleDebug
   ```

## Tests

| Area | Command (from repository root unless noted) |
| --- | --- |
| Build | `dotnet build SolarMicrogrid.slnx --configuration Release --no-restore -m:1` |
| Service tests | `dotnet test SolarMicrogrid.Tests/SolarMicrogrid.Tests.csproj --configuration Release --no-build --no-restore` |
| API + MongoDB integration | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\run-component3-tests.ps1` (starts and removes a disposable MongoDB 8 replica set in Docker) |
| Web lint, tests, build | `npm run check` in `SolarMicrogrid.Web` |
| Android Gradle | `.\gradlew.bat testDebugUnitTest assembleDebug lintDebug` in `SolarGridAndroid` (requires the Android SDK) |
| Android pure-Java supplemental | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\run-member4-android-unit-tests.ps1` |
| Local deployment contract | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\verify-member4-deployment.ps1` |

The latest recorded results are in [docs/member-4/final-audit.md](docs/member-4/final-audit.md).

## IIS deployment (summary)

1. Install IIS and the .NET 10 Hosting Bundle on the server.
2. Publish the API:

   ```powershell
   dotnet publish SolarMicrogrid.API/SolarMicrogrid.API.csproj -c Release -p:PublishProfile=IIS-Release
   ```

   Output goes to `artifacts\iis\api`, using ANCM v2 in-process with stdout logging off.
3. Create a dedicated app pool (No Managed Code) and site, bind a trusted HTTPS certificate, and restrict file permissions.
4. Set `ASPNETCORE_ENVIRONMENT=Production`, the MongoDB/JWT secrets and `Cors__AllowedOrigins__0` as environment variables.
5. Build the web client with `VITE_API_BASE_URL` (`/api` for same-origin). Build Android release with `SOLARGRID_API_BASE_URL=https://<api-host>/api/`.
6. Check `https://<api-host>/health` and run the smoke tests.

The full runbook, including rollback and troubleshooting, is in [docs/deployment/iis-deployment.md](docs/deployment/iis-deployment.md).

## Team contributions

| Member | Component |
| --- | --- |
| Member 1 `<name / ID>` | Authentication, JWT roles, users/prosumers, Android SQLite session |
| Member 2 `<name / ID>` | Stations, slots, capacity, station location data |
| Member 3 `<name / ID>` | Reservation create/update/cancel/approve/reject and scheduling rules |
| Member 4 `<name / ID>` | Dashboards, booking history/search, secure QR verification and completion, deployment |

### Member 4 contribution

- **API**:
  - `GET /api/dashboard` and `GET /api/dashboard/history`: role-scoped counts, current/pending/approved-future lists, and history with search/status/station/date filters and stable paging.
  - `POST /api/transactions/reservations/{id}/qr`, `POST /api/transactions/verify` and `POST /api/transactions/reservations/{id}/complete`:
    - opaque 256-bit tokens with hash-only storage and a five-minute expiry;
    - assigned-station verification;
    - atomic one-time completion with replay prevention.
- **Web**: Grid Operator dashboard (`/operator/dashboard`) and booking history (`/operator/history`).
- **Android**:
  - Prosumer dashboard, booking history and QR display.
  - Grid Operator CameraX/ML Kit scanner, verification, confirmation and completion screens.
- **Deployment**: bounded `/health`, validated CORS, OpenAPI with bearer security, correlation IDs, safe error logging, IIS publish profile and runbook, and removal of a previously tracked JWT key.
- **Documentation**: [contracts](docs/member-4/contracts.md), [progress](docs/member-4/progress.md), [test evidence](docs/member-4/test-evidence.md), [final audit](docs/member-4/final-audit.md), [screenshot checklist](docs/member-4/screenshot-checklist.md), [video script](docs/member-4/video-script.md), [viva notes](docs/member-4/viva-notes.md).

## Troubleshooting

- **API fails at startup with a configuration error**: the JWT key or MongoDB connection string is missing. Set them through user secrets or environment variables.
- **Transaction/completion errors or integration tests fail to start**: MongoDB must be a replica set. Check that Docker is running for the test script.
- **PowerShell refuses to run a script**: use `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <script>` as shown above.
- **Browser CORS error**: the web origin must exactly match an entry in `Cors:AllowedOrigins` (scheme, host and port, no path or wildcard).
- **Android `SDK location not found`**: set `ANDROID_HOME` or create `SolarGridAndroid/local.properties` with `sdk.dir=...` (do not commit it).
- **Android cannot reach the API**: `10.0.2.2` works only from the emulator. Use a LAN IP for a device and a hosted HTTPS URL for release builds.
- **401 after some time**: the JWT expired (60 minutes by default). Sign in again.
- **403 on verify**: the Grid Operator's assigned station does not match the reservation's station.
- **409 on verify/complete**: the QR was already used, expired, or the reservation changed. Ask the Prosumer to reopen the QR.
- **IIS 500.30 / 502.5**: check that the .NET 10 Hosting Bundle is installed and see the runbook troubleshooting section.
