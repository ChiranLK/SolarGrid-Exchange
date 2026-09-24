export interface EligibleProsumer {
  nic: string
  fullName: string
  email: string
}

export interface PagedEligibleProsumers {
  items: EligibleProsumer[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}
