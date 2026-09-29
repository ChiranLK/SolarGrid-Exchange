import { Link } from 'react-router-dom'
import { StatusBadge } from '../../components/StatusBadge'
import {
  formatEnergy,
  formatSlotRange,
} from '../reservations/reservationFormatters'
import type { DashboardReservationSummary } from './dashboardTypes'

interface ReservationSummaryTableProps {
  items: DashboardReservationSummary[]
  returnTo: string
  emptyMessage: string
}

export function ReservationSummaryTable({
  items,
  returnTo,
  emptyMessage,
}: ReservationSummaryTableProps) {
  if (items.length === 0) {
    return <p className="text-body-secondary mb-0 py-2">{emptyMessage}</p>
  }

  return (
    <>
      <div className="table-responsive d-none d-lg-block">
        <table className="table align-middle mb-0 reservation-table">
          <thead>
            <tr>
              <th scope="col">Reference</th>
              <th scope="col">Prosumer</th>
              <th scope="col">Station</th>
              <th scope="col">Schedule</th>
              <th scope="col">Energy</th>
              <th scope="col">Status</th>
              <th scope="col"><span className="visually-hidden">Open</span></th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.reservationId}>
                <td className="fw-semibold">{item.reference}</td>
                <td>
                  <div>{item.prosumerFullName ?? 'Prosumer'}</div>
                  <div className="small text-body-secondary">{item.prosumerNic}</div>
                </td>
                <td>{item.stationName ?? item.stationId}</td>
                <td>{formatSlotRange(item.scheduledStartTimeUtc, item.scheduledEndTimeUtc)}</td>
                <td>{formatEnergy(item.requestedEnergyKwh)}</td>
                <td><StatusBadge status={item.status} /></td>
                <td className="text-end">
                  <Link
                    className="btn btn-sm btn-outline-success"
                    to={`/reservations/${encodeURIComponent(item.reservationId)}?returnTo=${encodeURIComponent(returnTo)}`}
                    aria-label={`View ${item.reference}`}
                  >
                    View
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="d-grid gap-3 d-lg-none">
        {items.map((item) => (
          <article key={item.reservationId} className="operator-reservation-card">
            <div className="d-flex justify-content-between align-items-start gap-3 mb-3">
              <div>
                <div className="fw-semibold">{item.reference}</div>
                <div className="small text-body-secondary">
                  {item.prosumerFullName ?? item.prosumerNic}
                </div>
              </div>
              <StatusBadge status={item.status} />
            </div>
            <dl className="reservation-card-details mb-3">
              <dt>Station</dt>
              <dd>{item.stationName ?? item.stationId}</dd>
              <dt>Schedule</dt>
              <dd>{formatSlotRange(item.scheduledStartTimeUtc, item.scheduledEndTimeUtc)}</dd>
              <dt>Energy</dt>
              <dd>{formatEnergy(item.requestedEnergyKwh)}</dd>
            </dl>
            <Link
              className="btn btn-sm btn-outline-success w-100"
              to={`/reservations/${encodeURIComponent(item.reservationId)}?returnTo=${encodeURIComponent(returnTo)}`}
            >
              View reservation
            </Link>
          </article>
        ))}
      </div>
    </>
  )
}
