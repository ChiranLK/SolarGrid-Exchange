# SolarGrid Exchange Web

Shared React UI foundation for the SolarGrid-Exchange group project. It is a client of the central ASP.NET Core Web API and never connects to MongoDB directly.

## Technology

- React and TypeScript with Vite
- Bootstrap 5 plus a small shared theme layer
- React Router
- One centralized Fetch-based API client
- Session-based JWT storage and API-backed session restoration

## Prerequisites

- Node.js 22.12 or later
- npm 11 or later
- The `SolarMicrogrid.API` project running with a valid MongoDB and JWT configuration

## Local setup

1. Start the API using its HTTPS launch profile:

   ```powershell
   dotnet run --project ../SolarMicrogrid.API --launch-profile https
   ```

2. Copy `.env.example` to `.env.local` if the defaults need changing. The defaults proxy `/api` to the API address declared in `SolarMicrogrid.API/Properties/launchSettings.json`.

3. Install and start the web app:

   ```powershell
   npm install
   npm run dev
   ```

4. Open `http://localhost:5173`.

Do not commit `.env.local`, tokens, passwords, or API secrets.

## Commands

| Command | Purpose |
| --- | --- |
| `npm run dev` | Run the Vite development server. |
| `npm run build` | Type-check and produce the optimized `dist` build. |
| `npm run lint` | Run ESLint across TypeScript and React source. |
| `npm run test` | Run focused Vitest checks. |
| `npm run check` | Run lint, focused tests, and the production build. |
| `npm run preview` | Preview an existing production build locally. |

## Application structure

```text
src/
  api/          Central HTTP client and endpoint modules
  auth/         Session types, storage, context, and API restoration
  components/   Shared loading, empty, alert, error, confirmation, and status UI
  config/       Environment configuration
  features/     Team-owned feature modules
    users/
    prosumers/
    stations/
    reservations/
    dashboard/
    operations/
  layouts/      Responsive authenticated application shell
  pages/        Shared login, home, dashboard, and HTTP-state pages
  routes/       Authentication and role route guards
  styles/       Bootstrap-compatible shared theme additions
```

Feature modules should call `apiRequest` through an endpoint module under `src/api`; they must not call `fetch` independently or reproduce API business/authorization rules.

## Shared routes

| Route | Access | Current integration |
| --- | --- | --- |
| `/login` | Anonymous | `POST /api/auth/login` |
| `/` | Authenticated | Responsive home and account summary |
| `/users` | Backoffice, GridOperator | Shared placeholder for confirmed user-management contracts |
| `/prosumers` | Backoffice, GridOperator | Placeholder; dedicated controller is currently empty |
| `/stations` | Authenticated | Shared station/slot feature entry point |
| `/reservations` | Authenticated | Member 3 feature integration already present on the current baseline |
| `/dashboard` | Authenticated | Placeholder; dashboard controller is currently empty |
| `/operations` | Backoffice, GridOperator | Shared operational workspace placeholder |

## Verified API integrations

| API route | Web usage |
| --- | --- |
| `POST /api/auth/login` | Login form; stores the returned token and user summary in `sessionStorage`. |
| `GET /api/auth/me` | Validates a stored token before restoring a protected session. |
| `GET /api/reservations` | Role-scoped list, views, station/status filters, Backoffice search, and pagination. |
| `GET /api/reservations/{id}` | Object-authorized booking details, audit history, and server-derived actions. |
| `GET /api/users/eligible-prosumers` | Staff-only, paged active-Prosumer search for the reservation owner picker; no full user-directory download. |
| `POST /api/reservations/staff` | Reviewed staff booking with one logical idempotency key, timeout reconciliation, and a dedicated saved summary. |
| Reservation create/update/cancel/approve/reject routes | Accessible action forms submit expected versions and idempotency keys; the API remains authoritative. |
| Station and available-slot reads | Populate reservation filters, creation, and rescheduling controls. |

The web guards use the API's exact role names: `Backoffice`, `GridOperator`, and `Prosumer`. They improve navigation and user experience only. Every protected backend operation must retain its API authorization attribute and object-level checks.

## Deliberately pending integrations

- `DashboardController` and `ProsumersController` are empty, so dashboard and Prosumer-specific data calls are not implemented.
- Registration exists at `POST /api/auth/register`, but a registration screen is outside this foundation task.
- User administration and station-management screens remain owned follow-up work. The reservation feature only reuses the authorized eligible-Prosumer lookup; it does not duplicate user management.
- The API has no logout or refresh-token endpoint. Sign-out therefore removes the browser session locally; token expiry is handled on the next API request.
- The API currently has no cross-origin CORS policy. Local development uses Vite's same-origin `/api` proxy. A separately hosted production UI requires an explicit trusted-origin API policy or same-origin reverse proxy.
- Dashboard metrics, QR verification, and reservation completion endpoints are not available and must not be simulated in the client.

## Deployment note

`VITE_API_BASE_URL` is embedded at build time. Prefer `/api` behind a same-origin reverse proxy. If an absolute API URL is used, the API must explicitly allow the deployed web origin.
