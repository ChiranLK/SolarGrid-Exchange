const dateTimeFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
})

const dateFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
})

const timeFormatter = new Intl.DateTimeFormat(undefined, {
  timeStyle: 'short',
})

export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—'
  }

  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? '—' : dateTimeFormatter.format(date)
}

export function formatSlotRange(startValue: string, endValue: string): string {
  const start = new Date(startValue)
  const end = new Date(endValue)
  if (Number.isNaN(start.valueOf()) || Number.isNaN(end.valueOf())) {
    return 'Schedule unavailable'
  }

  const sameLocalDay = start.toDateString() === end.toDateString()
  return sameLocalDay
    ? `${dateFormatter.format(start)}, ${timeFormatter.format(start)}–${timeFormatter.format(end)}`
    : `${dateTimeFormatter.format(start)}–${dateTimeFormatter.format(end)}`
}

export function formatReservationReference(reservationId: string): string {
  return `RES-${reservationId.slice(-8).toUpperCase()}`
}

export function formatEnergy(value: number): string {
  return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value)} kWh`
}
