# Reservations feature

Component 3 reservation list, detail, creation, update, cancellation, approval, and rejection UI. Staff creation uses the bounded `/api/users/eligible-prosumers` search, active stations, available slots, a review step, one idempotency key per logical booking, timeout reconciliation, and a saved summary. Lifecycle, eligibility, schedule, overlap, and capacity authorization remain in the API.
