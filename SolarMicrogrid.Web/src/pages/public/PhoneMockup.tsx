import { CalendarIcon, HomeIcon, MapPinIcon, QrIcon, SolarGridLogoIcon, UsersIcon } from '../../components/icons'

const nearbyStations = [
  { name: 'Kottawa Solar Hub', distance: '2.4 km', slots: '3 slots today' },
  { name: 'Maharagama Community Array', distance: '5.1 km', slots: '5 slots today' },
  { name: 'Nugegoda Rooftop Station', distance: '6.8 km', slots: 'Full today' },
]

/** Decorative preview of the Android app's home screen. */
export function PhoneMockup() {
  return (
    <div className="phone-stage" aria-hidden="true">
      <div className="phone-glow" />
      <div className="phone">
        <div className="phone-notch" />
        <div className="phone-screen">
          <div className="phone-status">
            <span>9:41</span>
            <span className="phone-status-icons"><i /><i /><i /></span>
          </div>

          <div className="phone-head">
            <span className="phone-logo"><SolarGridLogoIcon /></span>
            <div>
              <small>Good morning</small>
              <strong>Kavindi</strong>
            </div>
          </div>

          <div className="phone-card phone-card-hero">
            <div className="phone-card-top">
              <small>Upcoming booking</small>
              <span className="phone-pill">Approved</span>
            </div>
            <strong>Kottawa Solar Hub</strong>
            <span>Thursday, 10:00 to 11:00</span>
            <div className="phone-card-meta">
              <span><b>4.5</b> kWh</span>
              <span className="phone-qr-btn"><QrIcon width={13} height={13} /> Show QR</span>
            </div>
          </div>

          <p className="phone-section-title">Stations near you</p>
          {nearbyStations.map((station) => (
            <div key={station.name} className="phone-row">
              <span className="phone-row-icon"><MapPinIcon width={13} height={13} /></span>
              <span className="phone-row-text">
                <strong>{station.name}</strong>
                <small>{station.distance} away</small>
              </span>
              <span className={`phone-row-tag ${station.slots.startsWith('Full') ? 'is-full' : ''}`}>{station.slots}</span>
            </div>
          ))}

          <div className="phone-tabbar">
            <span className="is-active"><HomeIcon width={15} height={15} /></span>
            <span><MapPinIcon width={15} height={15} /></span>
            <span><CalendarIcon width={15} height={15} /></span>
            <span><UsersIcon width={15} height={15} /></span>
          </div>
        </div>
      </div>
    </div>
  )
}
