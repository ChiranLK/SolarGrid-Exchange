import type { SlotInput, SlotSummary } from './stationTypes'

export interface SlotFormValues {
  startUtc: string
  endUtc: string
  totalCapacityKwh: string
}

export function initialSlotValues(slot?: SlotSummary): SlotFormValues {
  return {
    startUtc: slot?.startTimeUtc.slice(0, 16) ?? '',
    endUtc: slot?.endTimeUtc.slice(0, 16) ?? '',
    totalCapacityKwh: slot ? String(slot.totalCapacityKwh) : '',
  }
}

export function validateSlot(values: SlotFormValues, now = new Date()): string[] {
  const errors: string[] = []
  const start = parseUtcMinute(values.startUtc)
  const end = parseUtcMinute(values.endUtc)
  if (!start || start <= now) errors.push('Start must be a valid future UTC date and time.')
  if (!end || !start || end <= start) errors.push('End must be after start.')
  const capacity = Number(values.totalCapacityKwh)
  if (!values.totalCapacityKwh.trim() || !Number.isFinite(capacity) || capacity < 0.01) {
    errors.push('Total capacity must be at least 0.01 kWh.')
  }
  return errors
}

export function toSlotInput(values: SlotFormValues): SlotInput {
  return {
    startTimeUtc: parseUtcMinute(values.startUtc)!.toISOString(),
    endTimeUtc: parseUtcMinute(values.endUtc)!.toISOString(),
    totalCapacityKwh: Number(values.totalCapacityKwh),
  }
}

function parseUtcMinute(value: string): Date | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value)) return null
  const date = new Date(`${value}:00Z`)
  return !Number.isNaN(date.getTime()) && date.toISOString().slice(0, 16) === value ? date : null
}
