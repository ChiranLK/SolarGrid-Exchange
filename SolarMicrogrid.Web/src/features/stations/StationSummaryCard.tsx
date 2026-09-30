import { Link } from 'react-router-dom'
import { MapPinIcon, SunIcon } from '../../components/icons'
import { StatusBadge } from '../../components/StatusBadge'
import type { Station } from './stationTypes'

export function StationSummaryCard({ station }: { station: Station }) {
  return (
    <article className="card border-0 shadow-sm h-100 station-summary-card">
      <div className="card-body p-4 d-flex flex-column">
        <div className="d-flex justify-content-between gap-2 align-items-start mb-4">
          <span className="station-solar" aria-hidden="true"><SunIcon width={22} height={22} /></span>
          <StatusBadge status={station.isActive ? 'Active' : 'Deactivated'} />
        </div>
        <h2 className="h5 mb-2"><Link to={`/stations/${encodeURIComponent(station.id)}`}>{station.name}</Link></h2>
        <p className="text-body-secondary small d-flex align-items-start gap-1 mb-3">
          <MapPinIcon width={16} height={16} className="flex-shrink-0 mt-1" />
          <span>{station.address}</span>
        </p>
        <dl className="row small mb-0 mt-auto">
          <dt className="col-7">Generation</dt><dd className="col-5 text-end">{station.energyGenerationCapacityKw} kW</dd>
          <dt className="col-7">Battery storage</dt><dd className="col-5 text-end">{station.batteryStorageCapacityKwh} kWh</dd>
        </dl>
      </div>
    </article>
  )
}
