# Users feature (Member 1)

Backoffice account administration. Every rule (status transitions, self-deactivation, last-Backoffice
protection, station validity) is enforced by the API under `/api/users`; the UI only hides actions
that cannot apply and shows the API's message when a request is refused.

| Route | Screen | API |
| --- | --- | --- |
| `/users` | All users, role/status filters (server side), text filter (loaded rows) | `GET /api/users?role=&status=` |
| `/users/new` | Create Backoffice / Grid Operator account, optional active station | `POST /api/users`, `GET /api/stations?isActive=true` |
| `/users/pending` | Approve pending Prosumers | `GET /api/users/pending`, `PATCH /api/users/{nic}/activate` |
| `/users/deactivation-requests` | Approve Prosumer deactivation requests | `GET /api/users/deactivation-requests`, `PATCH /api/users/{nic}/deactivate` |
| (dialog on `/users`) | Activate / reactivate / deactivate, assign or change a Grid Operator's station | `PATCH /api/users/{nic}/activate`, `/deactivate`, `/station` |

All routes are Backoffice-only (`RoleRoute`). `UserAdminLayout` shares a refresh signal so that a
successful action reloads the current list and the tab counts.

The eligible-prosumer search in `userApi.ts` belongs to Member 3's reservation screens.
