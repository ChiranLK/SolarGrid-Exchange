# IIS deployment runbook

Status: code/configuration verified locally; hosted IIS, HTTPS, MongoDB networking, and live client smoke tests are **Not Verified** because no deployment target or credentials are available in this workspace.

This runbook deploys the existing .NET 10 API without changing its FAT service architecture. It never puts a connection string, JWT key, QR value, NIC, or other secret in source control.

## Prerequisites

- Windows Server with IIS, Management Tools, HTTPS binding support, and the current .NET 10 Hosting Bundle installed. After installing or updating the bundle, run `iisreset` during an approved maintenance window.
- A trusted TLS certificate for the public API hostname. Redirect HTTP to HTTPS at IIS/load-balancer level and expose only HTTPS publicly.
- MongoDB reachable from the IIS host. Production transactions require a replica set. Allow only the IIS host/network, use TLS, a least-privilege application user, and the required DNS/port route.
- A deployment identity that can copy release files and configure the site, app pool, certificate, and environment values.
- A release directory outside the repository, for example `C:\inetpub\SolarGrid\releases\<release-id>`.

Confirm the runtime and module before copying files:

```powershell
dotnet --list-runtimes
Get-WindowsFeature Web-Server
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2' -ErrorAction Stop
```

The runtime list must contain `Microsoft.AspNetCore.App 10.*`. Install/repair the .NET 10 Hosting Bundle if the registry/module check fails.

## Build and publish

From the repository root:

```powershell
dotnet restore SolarMicrogrid.slnx
dotnet build SolarMicrogrid.slnx --configuration Release --no-restore -m:1
dotnet test SolarMicrogrid.slnx --configuration Release --no-build --no-restore -m:1
dotnet publish SolarMicrogrid.API/SolarMicrogrid.API.csproj --configuration Release --no-restore -p:PublishProfile=IIS-Release
```

The profile publishes a framework-dependent .NET 10 package to `artifacts\iis\api`. Verify it before transfer:

```powershell
Test-Path artifacts\iis\api\SolarMicrogrid.API.dll
Test-Path artifacts\iis\api\web.config
[xml]$webConfig = Get-Content -Raw artifacts\iis\api\web.config
$webConfig.configuration.location.'system.webServer'.aspNetCore
Get-FileHash artifacts\iis\api\SolarMicrogrid.API.dll -Algorithm SHA256
```

For a disposable local MongoDB/OpenAPI/health/CORS check (Docker Desktop required), run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\verify-member4-deployment.ps1
```

The generated `web.config` must use `AspNetCoreModuleV2`, `dotnet`, `SolarMicrogrid.API.dll`, and `hostingModel="inprocess"`. It intentionally contains no application secret and keeps ANCM stdout logging disabled.

## IIS app pool and site

Create a dedicated app pool with **No Managed Code**, Integrated pipeline, 64-bit enabled, and an application-pool identity. Example administrative commands:

```powershell
Import-Module WebAdministration
New-WebAppPool -Name 'SolarGridApi'
Set-ItemProperty IIS:\AppPools\SolarGridApi -Name managedRuntimeVersion -Value ''
Set-ItemProperty IIS:\AppPools\SolarGridApi -Name managedPipelineMode -Value Integrated
Set-ItemProperty IIS:\AppPools\SolarGridApi -Name enable32BitAppOnWin64 -Value $false
New-Website -Name 'SolarGridApi' -ApplicationPool 'SolarGridApi' -PhysicalPath 'C:\inetpub\SolarGrid\current' -Port 443 -Ssl
```

Bind the real hostname and certificate in IIS Manager or with the organization's approved certificate automation. Do not use a development certificate.

Grant `IIS AppPool\SolarGridApi` read/execute access to the release/current directory. Do not grant Modify to application binaries. If temporary ANCM stdout logging is enabled for diagnosis, create a separate `logs` directory, grant Modify only there, rotate/restrict it, and disable stdout logging immediately after diagnosis. Normal diagnostics should use Windows Event Viewer, IIS request logs, and the organization's restricted structured-log sink.

## Environment configuration

Set values at the IIS application/app-pool or machine secret-provider layer. Secret values must not be placed in `web.config`, `appsettings*.json`, source control, screenshots, tickets, or shell history. Required/operational key names are:

- `ASPNETCORE_ENVIRONMENT`
- `MongoSettings__ConnectionString`
- `MongoSettings__DatabaseName`
- `MongoSettings__UsersCollectionName`
- `MongoSettings__StationsCollectionName`
- `MongoSettings__SlotsCollectionName`
- `MongoSettings__ReservationsCollectionName`
- `MongoSettings__ReservationSchedulingGuardsCollectionName`
- `MongoSettings__QrTransactionsCollectionName`
- `JwtSettings__Key`
- `JwtSettings__Issuer`
- `JwtSettings__Audience`
- `JwtSettings__ExpirationMinutes`
- `Cors__AllowedOrigins__0` (then `__1`, `__2`, as required)
- `Deployment__MongoHealthTimeoutSeconds`
- `OpenApi__Enabled`
- `TransactionSettings__QrTokenLifetimeMinutes`
- `TransactionSettings__VerificationLifetimeMinutes`

Use `Production` for the ASP.NET Core environment. The JWT key must be a rotated, high-entropy secret of at least 32 characters. Production CORS origins must be exact HTTPS origins such as the deployed web client's scheme/host/port, with no path or trailing wildcard. Leave the origin array empty for a truly same-origin reverse-proxy deployment. This API uses bearer headers, not credentialed browser cookies, so the CORS policy does not enable credentials.

OpenAPI is disabled by default in production. Enable it only for an approved verification window or protect `/openapi` at IIS/network level, then disable it again.

After configuration, recycle only the dedicated pool:

```powershell
Restart-WebAppPool -Name 'SolarGridApi'
Get-WebAppPoolState -Name 'SolarGridApi'
Get-Website -Name 'SolarGridApi'
```

## Health and OpenAPI verification

The unauthenticated deployment check is `GET /health`. It verifies the API process and a bounded MongoDB ping. It returns HTTP 200 only when both are healthy and HTTP 503 otherwise. Its JSON contains only `status`, the `api`/`database` states, and `correlationId`; it never exposes configuration or exception detail.

```powershell
$health = Invoke-WebRequest -Uri 'https://api-host.example/health' -UseBasicParsing
$health.StatusCode
$health.Headers['X-Correlation-ID']
$health.Content
```

When `OpenApi__Enabled` is temporarily true, download the generated Swagger/OpenAPI document:

```powershell
$document = Invoke-RestMethod -Uri 'https://api-host.example/openapi/v1.json'
$document.components.securitySchemes.Bearer
$document.paths.'/api/dashboard'.get
$document.paths.'/api/dashboard/history'.get
$document.paths.'/api/transactions/reservations/{reservationId}/qr'.post
$document.paths.'/api/transactions/verify'.post
$document.paths.'/api/transactions/reservations/{reservationId}/complete'.post
```

Verify each Member 4 operation shows bearer security, exact roles in its description, request/response schemas, declared 200/400/401/403/404/409/500 responses as applicable, and redacted representative examples. Never paste a production JWT, QR token, verification receipt, or NIC into Swagger tooling or logs.

Verify CORS from the deployed web origin:

```powershell
curl.exe -i -X OPTIONS "https://api-host.example/api/dashboard" -H "Origin: https://web-host.example" -H "Access-Control-Request-Method: GET" -H "Access-Control-Request-Headers: authorization"
```

The response must allow only the configured origin. Repeat with an unlisted origin and confirm no `Access-Control-Allow-Origin` header is returned.

## Hosted client URLs

The web build reads `VITE_API_BASE_URL` at build time. Prefer `/api` when IIS/reverse proxy routes the API on the same origin; otherwise use the hosted HTTPS API origin and add the web origin to CORS.

```powershell
$env:VITE_API_BASE_URL = '/api'
npm.cmd --prefix SolarMicrogrid.Web ci
npm.cmd --prefix SolarMicrogrid.Web run build
```

Android release builds require `SOLARGRID_API_BASE_URL`, reject loopback/HTTP values, and require a hosted HTTPS URL ending in `/api/`:

```powershell
$env:SOLARGRID_API_BASE_URL = 'https://api-host.example/api/'
Push-Location SolarGridAndroid
.\gradlew.bat assembleRelease
Pop-Location
```

Android emulator `127.0.0.1`/`localhost` refers to the emulator itself; `10.0.2.2` reaches the development host from the standard emulator. Both are debug-only. A physical development device needs a reachable LAN address. Release builds must use the public HTTPS API URL and a certificate trusted by Android.

## Smoke tests

Run with sanitized deployment accounts and never print bearer/QR values:

1. Confirm HTTPS certificate/hostname validity and that HTTP redirects to HTTPS.
2. Confirm `/health` returns 200 with `api` and `database` healthy; stop or firewall MongoDB in an approved test environment and confirm the bounded response becomes 503 without internal detail.
3. Confirm the OpenAPI checks above during the approved window, then disable it and recycle the pool.
4. Sign in separately as Prosumer, Grid Operator, and Backoffice; confirm unauthorized and wrong-role requests return 401/403.
5. Load dashboard and history for each permitted role and confirm server scope/paging.
6. In a sanitized reservation, issue QR, verify at the assigned station, complete once, and confirm replay/conflict behavior without recording the values.
7. Load the production web build and Android release build against the hosted API; confirm no request targets localhost or `10.0.2.2`.
8. Review response `X-Correlation-ID`, IIS logs, and application logs; confirm no Authorization header, JWT, QR/receipt, NIC, connection string, or secret was recorded.

## Rollback

Keep the prior immutable release directory and its SHA-256 inventory. If smoke tests fail:

```powershell
Stop-WebAppPool -Name 'SolarGridApi'
Set-ItemProperty 'IIS:\Sites\SolarGridApi' -Name physicalPath -Value 'C:\inetpub\SolarGrid\releases\<previous-release-id>'
Start-WebAppPool -Name 'SolarGridApi'
Invoke-WebRequest -Uri 'https://api-host.example/health' -UseBasicParsing
```

Rollback configuration/secret versions through the same secret-management process. Do not restore an old compromised JWT key. This release does not run a database migration, but it creates idempotent MongoDB indexes at startup; validate index compatibility before any future rollback that changes schemas.

## Troubleshooting

- **500.30/502.5 or pool stops:** check Event Viewer and the Hosting Bundle/ANCM installation; confirm `dotnet --list-runtimes` includes ASP.NET Core 10 and that the app-pool identity can read the release.
- **Health is 503:** test DNS/TLS/firewall reachability from the IIS host, replica-set state, MongoDB allow-list, and application-user permissions. Do not paste the connection string into logs.
- **Health times out:** confirm `Deployment__MongoHealthTimeoutSeconds` is 1–30 and inspect network/server-selection latency. The public response intentionally omits topology detail.
- **401:** verify issuer/audience/key alignment and server clock without logging a token.
- **403:** verify the persisted active role and assigned station; do not rely on client navigation.
- **Browser CORS failure:** compare the browser `Origin` exactly with `Cors__AllowedOrigins__N`, including scheme and port. Paths and wildcards are invalid.
- **HTTPS redirect loop:** ensure IIS terminates HTTPS correctly and the public binding/proxy forwarding is consistent.
- **OpenAPI 404:** it is disabled unless `OpenApi__Enabled` is true; enable only for the verification window and recycle the pool.
- **Android cannot connect:** confirm the release URL is HTTPS and ends `/api/`; `10.0.2.2` is only the emulator-to-development-host alias.
- **Need temporary ANCM stdout logs:** grant Modify only to the dedicated log directory, enable `stdoutLogEnabled` briefly, reproduce once, disable it, recycle, secure/delete logs per policy, and verify they contain no secrets.
