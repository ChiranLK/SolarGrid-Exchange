import { Link } from 'react-router-dom'
import { StatusBadge } from '../../components/StatusBadge'
import type { Station } from './stationTypes'

export function StationSummaryCard({ station }: { station: Station }) {
  return (
    <article className="card border-0 shadow-sm h-100">
      <div className="card-body">
        <div className="d-flex justify-content-between gap-2 align-items-start">
          <h2 className="h5 mb-2"><Link to={`/stations/${encodeURIComponent(station.id)}`}>{station.name}</Link></h2>
          <StatusBadge status={station.isActive ? 'Active' : 'Deactivated'} />
        </div>
        <p className="text-body-secondary mb-3">{station.address}</p>
        <dl className="row small mb-0">
          <dt className="col-7">Generation</dt><dd className="col-5 text-end">{station.energyGenerationCapacityKw} kW</dd>
          <dt className="col-7">Battery storage</dt><dd className="col-5 text-end">{station.batteryStorageCapacityKwh} kWh</dd>
        </dl>
      </div>
    </article>
  )
}
