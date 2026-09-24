export interface StationSummary {
  id: string
  name: string
  address: string
  isActive: boolean
}

export interface PagedStations {
  items: StationSummary[]
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
}

export interface PagedSlots {
  items: SlotSummary[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}
