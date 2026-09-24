import { ApiError } from '../../api/apiClient'
import type { CreateReservationRequest } from './reservationTypes'

export interface CreationErrorPresentation {
  title: string
  message: string
  outcomeUnknown: boolean
}

export function getCreationFingerprint(request: CreateReservationRequest): string {
  return JSON.stringify({
    targetProsumerNic: request.targetProsumerNic?.trim().toUpperCase() ?? '',
    slotId: request.slotId,
    requestedEnergyKwh: request.requestedEnergyKwh,
  })
}

export function describeCreationError(error: unknown): CreationErrorPresentation {
  if (!(error instanceof ApiError)) {
    return {
      title: 'Reservation not saved',
      message: 'The reservation could not be saved. Your selections have been kept so you can try again.',
      outcomeUnknown: false,
    }
  }

  if (error.status === 0) {
    return {
      title: 'Save outcome not confirmed',
      message: 'The API could not be reached after a safe retry with the same request identifier. The reservation may have been saved; retry to reconcile it without creating a duplicate.',
      outcomeUnknown: true,
    }
  }

  const normalized = error.message.toLowerCase()
  if (normalized.includes('eligible active prosumer') || normalized.includes('prosumer does not exist')) {
    return {
      title: 'Prosumer is not eligible',
      message: `${error.message} Search again and select an active Prosumer.`,
      outcomeUnknown: false,
    }
  }
  if (
    normalized.includes('seven') ||
    normalized.includes('7 day') ||
    normalized.includes('days ahead') ||
    normalized.includes('booking horizon')
  ) {
    return {
      title: 'Slot is outside the booking window',
      message: `${error.message} Select a slot no more than seven days from the server's current time.`,
      outcomeUnknown: false,
    }
  }
  if (normalized.includes('duplicate') || normalized.includes('overlap')) {
    return {
      title: 'Conflicting reservation',
      message: `${error.message} Review the Prosumer's existing active bookings before trying another slot.`,
      outcomeUnknown: false,
    }
  }
  if (
    normalized.includes('capacity') ||
    normalized.includes('allocation') ||
    normalized.includes('fully booked') ||
    normalized.includes('available energy')
  ) {
    return {
      title: 'Capacity is no longer available',
      message: `${error.message} Reload the station's slots and select current availability.`,
      outcomeUnknown: false,
    }
  }
  if (normalized.includes('slot') || error.status === 404) {
    return {
      title: 'Slot is no longer valid',
      message: `${error.message} Reload available slots and make a new selection.`,
      outcomeUnknown: false,
    }
  }

  return {
    title: 'Reservation not saved',
    message: `${error.message} Your selections have been kept so you can correct the issue or retry.`,
    outcomeUnknown: false,
  }
}
