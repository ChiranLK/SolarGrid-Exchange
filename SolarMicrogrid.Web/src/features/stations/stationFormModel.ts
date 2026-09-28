import type { OperatingSchedule, Station, StationInput } from './stationTypes'

export const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const

export interface StationFormValues {
  name: string
  description: string
  address: string
  latitude: string
  longitude: string
  energyGenerationCapacityKw: string
  batteryStorageCapacityKwh: string
  operatingSchedule: OperatingSchedule[]
}

export function initialStationValues(station?: Station): StationFormValues {
  return {
    name: station?.name ?? '',
    description: station?.description ?? '',
    address: station?.address ?? '',
    latitude: station ? String(station.latitude) : '',
    longitude: station ? String(station.longitude) : '',
    energyGenerationCapacityKw: station ? String(station.energyGenerationCapacityKw) : '',
    batteryStorageCapacityKwh: station ? String(station.batteryStorageCapacityKwh) : '',
    operatingSchedule: days.map((dayOfWeek) => ({
      dayOfWeek,
      isOpen: false,
      openingTime: null,
      closingTime: null,
      ...station?.operatingSchedule.find((item) => item.dayOfWeek === dayOfWeek),
    })),
  }
}

export function validateStation(values: StationFormValues): string[] {
  const errors: string[] = []
  if (!values.name.trim() || values.name.trim().length > 120) errors.push('Name is required and must be at most 120 characters.')
  if (values.description.trim().length > 500) errors.push('Description must be at most 500 characters.')
  if (!values.address.trim() || values.address.trim().length > 250) errors.push('Address is required and must be at most 250 characters.')
  const numbers: Array<[string, string, number, number]> = [
    ['Latitude', values.latitude, -90, 90],
    ['Longitude', values.longitude, -180, 180],
    ['Generation capacity (kW)', values.energyGenerationCapacityKw, 0.01, Infinity],
    ['Battery capacity (kWh)', values.batteryStorageCapacityKwh, 0, Infinity],
  ]
  for (const [label, raw, minimum, maximum] of numbers) {
    const value = Number(raw)
    if (!raw.trim() || !Number.isFinite(value) || value < minimum || value > maximum) {
      errors.push(`${label} must be between ${minimum} and ${maximum === Infinity ? 'a positive finite value' : maximum}.`)
    }
  }
  if (values.operatingSchedule.length < 1 || values.operatingSchedule.length > 7) {
    errors.push('Provide between one and seven schedule days.')
  }
  const seen = new Set<string>()
  for (const schedule of values.operatingSchedule) {
    if (!days.some((day) => day === schedule.dayOfWeek)) errors.push('Select a valid schedule day.')
    if (seen.has(schedule.dayOfWeek)) errors.push(`Duplicate schedule day: ${schedule.dayOfWeek}.`)
    seen.add(schedule.dayOfWeek)
    if (schedule.isOpen && (!isTime(schedule.openingTime) || !isTime(schedule.closingTime) || schedule.closingTime! <= schedule.openingTime!)) {
      errors.push(`${schedule.dayOfWeek} needs valid HH:mm times with closing after opening.`)
    }
  }
  return errors
}

export function toStationInput(values: StationFormValues): StationInput {
  return {
    name: values.name.trim(),
    description: values.description.trim() || null,
    address: values.address.trim(),
    latitude: Number(values.latitude),
    longitude: Number(values.longitude),
    energyGenerationCapacityKw: Number(values.energyGenerationCapacityKw),
    batteryStorageCapacityKwh: Number(values.batteryStorageCapacityKwh),
    operatingSchedule: values.operatingSchedule.map((item) => ({
      ...item,
      openingTime: item.isOpen ? item.openingTime : null,
      closingTime: item.isOpen ? item.closingTime : null,
    })),
  }
}

function isTime(value: string | null): boolean {
  return value !== null && /^(?:[01]\d|2[0-3]):[0-5]\d$/.test(value)
}
