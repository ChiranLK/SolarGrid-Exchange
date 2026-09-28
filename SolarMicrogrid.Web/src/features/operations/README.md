# Operations feature

Grid Operator dashboard and booking history live here and remain UI-only clients of the central API.

- `/operator/dashboard` renders live role/station-scoped counts and bounded operational summaries from `GET /api/dashboard`.
- `/operator/history` sends search, exact status, station, inclusive UTC date, and paging parameters to `GET /api/dashboard/history` and preserves server ordering.
- Both routes use the shared `ProtectedRoute` plus an exact `GridOperator` `RoleRoute`; the API remains the authorization boundary.
- Pages reuse the shared Fetch client, session, Bootstrap theme, status/loading/empty/error/pagination components, and reservation-change refresh events.
- Reservation workflow mutations remain under the Member 3 feature and are not duplicated here.
