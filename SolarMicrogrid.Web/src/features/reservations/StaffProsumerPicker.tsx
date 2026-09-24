import { useRef, useState, type FormEvent } from 'react'
import { userApi } from '../../api/userApi'
import { ApiErrorState } from '../../components/ApiErrorState'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { Pagination } from '../../components/Pagination'
import type { EligibleProsumer, PagedEligibleProsumers } from '../users/userTypes'

interface StaffProsumerPickerProps {
  selected: EligibleProsumer | null
  disabled?: boolean
  onSelect: (prosumer: EligibleProsumer) => void
}

export function StaffProsumerPicker({
  selected,
  disabled = false,
  onSelect,
}: StaffProsumerPickerProps) {
  const [searchInput, setSearchInput] = useState('')
  const [searchTerm, setSearchTerm] = useState('')
  const [results, setResults] = useState<PagedEligibleProsumers | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const activeController = useRef<AbortController | null>(null)

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const normalized = searchInput.trim()
    if (normalized.length < 2) {
      setError(new Error('Enter at least two characters of a name, NIC, or email address.'))
      setResults(null)
      return
    }
    setSearchTerm(normalized)
    void loadPage(normalized, 1)
  }

  async function loadPage(term: string, page: number) {
    activeController.current?.abort()
    const controller = new AbortController()
    activeController.current = controller
    setIsLoading(true)
    setError(null)
    try {
      const response = await userApi.searchEligibleProsumers(term, page, controller.signal)
      setResults(response)
    } catch (requestError) {
      if (!controller.signal.aborted) {
        setError(requestError)
      }
    } finally {
      if (!controller.signal.aborted) {
        setIsLoading(false)
      }
    }
  }

  return (
    <fieldset disabled={disabled}>
      <legend className="h6 mb-3">1. Select an eligible Prosumer</legend>
      {selected && (
        <div className="alert alert-success d-flex flex-wrap justify-content-between align-items-center gap-2">
          <div>
            <span className="fw-semibold">{selected.fullName}</span>
            <span className="d-block small">{selected.nic} · {selected.email}</span>
          </div>
          <span className="badge text-bg-success">Selected</span>
        </div>
      )}

      <form className="row g-2" role="search" onSubmit={handleSearch}>
        <div className="col-12 col-lg-9">
          <label htmlFor="eligible-prosumer-search" className="form-label">
            Search active Prosumers
          </label>
          <input
            id="eligible-prosumer-search"
            type="search"
            className="form-control"
            minLength={2}
            maxLength={100}
            placeholder="Name, NIC, or email"
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
            aria-describedby="eligible-prosumer-help"
          />
          <div id="eligible-prosumer-help" className="form-text">
            Only matching active Prosumer accounts are returned by the authorized API.
          </div>
        </div>
        <div className="col-12 col-lg-3 d-flex align-items-start pt-lg-4">
          <button
            type="submit"
            className="btn btn-outline-success w-100"
            disabled={isLoading || searchInput.trim().length < 2}
          >
            Search
          </button>
        </div>
      </form>

      {isLoading && <LoadingState label="Searching eligible Prosumers…" />}
      {!isLoading && Boolean(error) && (
        error instanceof Error && error.name === 'Error' && !(error as { status?: number }).status
          ? <div className="alert alert-warning mt-3 mb-0" role="alert">{error.message}</div>
          : (
              <div className="mt-3">
                <ApiErrorState
                  error={error}
                  resourceName="eligible Prosumers"
                  onRetry={() => void loadPage(searchTerm, results?.page ?? 1)}
                />
              </div>
            )
      )}
      {!isLoading && !error && results?.items.length === 0 && (
        <div className="mt-3">
          <EmptyState
            title="No eligible Prosumers found"
            description="Try another name, NIC, or email. Inactive and non-Prosumer accounts are excluded by the API."
          />
        </div>
      )}
      {!isLoading && !error && results && results.items.length > 0 && (
        <div className="mt-3">
          <p className="small text-body-secondary mb-2">
            {results.totalCount} matching eligible {results.totalCount === 1 ? 'Prosumer' : 'Prosumers'}
          </p>
          <div className="list-group mb-3" aria-label="Eligible Prosumer search results">
            {results.items.map((prosumer) => {
              const isSelected = selected?.nic === prosumer.nic
              return (
                <button
                  key={prosumer.nic}
                  type="button"
                  className={`list-group-item list-group-item-action d-flex justify-content-between align-items-center gap-3 ${isSelected ? 'active' : ''}`}
                  aria-pressed={isSelected}
                  onClick={() => onSelect(prosumer)}
                >
                  <span className="text-start">
                    <span className="d-block fw-semibold">{prosumer.fullName}</span>
                    <span className={`d-block small ${isSelected ? 'text-white-50' : 'text-body-secondary'}`}>
                      {prosumer.nic} · {prosumer.email}
                    </span>
                  </span>
                  <span>{isSelected ? 'Selected' : 'Select'}</span>
                </button>
              )
            })}
          </div>
          <Pagination
            page={results.page}
            totalPages={results.totalPages}
            totalCount={results.totalCount}
            onPageChange={(page) => void loadPage(searchTerm, page)}
          />
        </div>
      )}
    </fieldset>
  )
}
