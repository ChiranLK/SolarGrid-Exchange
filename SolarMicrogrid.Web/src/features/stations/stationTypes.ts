export interface StationSummary {
  id: string
  name: string
  address: string
  isActive: boolean
}

export interface OperatingSchedule {
  dayOfWeek: string
  isOpen: boolean
  openingTime: string | null
  closingTime: string | null
}

export interface Station extends StationSummary {
  description: string | null
  latitude: number
  longitude: number
  energyGenerationCapacityKw: number
  batteryStorageCapacityKwh: number
  operatingSchedule: OperatingSchedule[]
  createdAtUtc: string
  updatedAtUtc: string
}

export type StationInput = Pick<Station,
  'name' | 'description' | 'address' | 'latitude' | 'longitude' |
  'energyGenerationCapacityKw' | 'batteryStorageCapacityKwh' | 'operatingSchedule'
>

export interface StationListQuery {
  search?: string
  isActive?: boolean
  page: number
  pageSize: number
}

export interface PagedStations {
  items: Station[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface SlotSummary {
  id: string
  stationId: string
  startTimeUtc: string
  endTimeUtc: string
  totalCapacityKwh: number
  availableCapacityKwh: number
  availabilityStatus: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface SlotInput {
  startTimeUtc: string
  endTimeUtc: string
  totalCapacityKwh: number
}

export interface SlotListQuery {
  page: number
  pageSize: number
  status?: 'Available' | 'FullyBooked' | 'Unavailable'
}

export interface PagedSlots {
  items: SlotSummary[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}
