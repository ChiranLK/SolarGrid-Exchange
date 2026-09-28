import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { StationForm } from './StationForm'
import { initialStationValues, toStationInput, validateStation } from './stationFormModel'
import { StationSummaryCard } from './StationSummaryCard'
import type { Station } from './stationTypes'

const station: Station = {
  id: 'station-1', name: 'North Solar', address: 'Main Road', description: null,
  latitude: 6.9, longitude: 79.8, energyGenerationCapacityKw: 20,
  batteryStorageCapacityKwh: 45, isActive: true,
  operatingSchedule: [{ dayOfWeek: 'Monday', isOpen: true, openingTime: '08:00', closingTime: '17:00' }],
  createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
}

describe('station screen components', () => {
  it('shows API station name, status, and capacity units in the list card', () => {
    const html = renderToStaticMarkup(<MemoryRouter><StationSummaryCard station={station} /></MemoryRouter>)
    expect(html).toContain('North Solar')
    expect(html).toContain('href="/stations/station-1"')
    expect(html).toContain('20 kW')
    expect(html).toContain('45 kWh')
    expect(html).toContain('Status: Active')
  })

  it('renders coordinate and capacity fields and editable schedule controls', () => {
    const html = renderToStaticMarkup(<StationForm initialValues={initialStationValues(station)} submitLabel="Save changes" isSaving={false} onSubmit={() => {}} />)
    expect(html).toContain('Latitude (°)')
    expect(html).toContain('Longitude (°)')
    expect(html).toContain('Generation capacity (kW)')
    expect(html).toContain('Battery storage capacity (kWh)')
    expect(html).toContain('Monday open')
    expect(html).toContain('Save changes')
  })
})

describe('station form contract', () => {
  it('rejects invalid coordinates, capacities, and schedule times', () => {
    const values = initialStationValues(station)
    values.latitude = '91'
    values.longitude = '-181'
    values.energyGenerationCapacityKw = '0'
    values.batteryStorageCapacityKwh = '-1'
    values.operatingSchedule[2] = { dayOfWeek: 'Monday', isOpen: true, openingTime: '18:00', closingTime: '08:00' }
    const errors = validateStation(values).join(' ')
    expect(errors).toContain('Latitude')
    expect(errors).toContain('Longitude')
    expect(errors).toContain('Generation capacity')
    expect(errors).toContain('Battery capacity')
    expect(errors).toContain('Duplicate schedule day')
    expect(errors).toContain('closing after opening')
  })

  it('keeps coordinate order and clears hours on closed days', () => {
    const values = initialStationValues(station)
    values.operatingSchedule[1].isOpen = false
    const request = toStationInput(values)
    expect(request.latitude).toBe(6.9)
    expect(request.longitude).toBe(79.8)
    expect(request.operatingSchedule[1]).toMatchObject({ dayOfWeek: 'Monday', openingTime: null, closingTime: null })
  })
})
