import { useEffect, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { stationApi } from '../../api/stationApi'
import { useAuth } from '../../auth/useAuth'
import { ApiErrorState } from '../../components/ApiErrorState'
import { EmptyState } from '../../components/EmptyState'
import { LoadingState } from '../../components/LoadingState'
import { PageHeader } from '../../components/PageHeader'
import { Pagination } from '../../components/Pagination'
import type { PagedStations } from './stationTypes'
import { StationSummaryCard } from './StationSummaryCard'

const pageSize = 12

export function StationListPage() {
  const { session } = useAuth()
  const [parameters, setParameters] = useSearchParams()
  const [searchInput, setSearchInput] = useState(parameters.get('search') ?? '')
  const [results, setResults] = useState<PagedStations | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [retry, setRetry] = useState(0)
  const search = (parameters.get('search') ?? '').trim().slice(0, 120)
  const activeFilter = parameters.get('isActive')
  const pageValue = Number(parameters.get('page'))
  const page = Number.isSafeInteger(pageValue) && pageValue > 0 ? pageValue : 1

  useEffect(() => {
    document.title = 'Stations | SolarGrid Exchange'
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    stationApi.list({
      search: search || undefined,
      isActive: activeFilter === 'true' ? true : activeFilter === 'false' ? false : undefined,
      page,
      pageSize,
    }, controller.signal)
      .then((response) => { setResults(response); setError(null) })
      .catch((requestError: unknown) => { if (!controller.signal.aborted) setError(requestError) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [search, activeFilter, page, retry])

  function updateFilter(key: string, value: string) {
    setLoading(true)
    setError(null)
    setParameters((current) => {
      const next = new URLSearchParams(current)
      if (value) next.set(key, value)
      else next.delete(key)
      if (key !== 'page') next.delete('page')
      return next
    })
  }

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    updateFilter('search', searchInput.trim())
  }

  return (
    <>
      <PageHeader eyebrow="Solar network" title="Stations" description="Find stations by name or address. Generation capacity, battery storage and status are shown for each station."
        actions={session?.role === 'Backoffice' ? <Link to="/stations/new" className="btn btn-success">Create station</Link> : undefined} />
      <form className="card border-0 shadow-sm mb-4" onSubmit={submitSearch} role="search">
        <div className="card-body row g-3 align-items-end">
          <div className="col-12 col-md-6">
            <label className="form-label" htmlFor="station-search">Name or address</label>
            <input id="station-search" className="form-control" value={searchInput} maxLength={120} onChange={(event) => setSearchInput(event.target.value)} />
          </div>
          <div className="col-12 col-sm-6 col-md-3">
            <label className="form-label" htmlFor="station-status">Status</label>
            <select id="station-status" className="form-select" value={activeFilter === 'true' || activeFilter === 'false' ? activeFilter : ''}
              onChange={(event) => updateFilter('isActive', event.target.value)}>
              <option value="">All</option><option value="true">Active</option><option value="false">Inactive</option>
            </select>
          </div>
          <div className="col-12 col-sm-6 col-md-3"><button className="btn btn-outline-success w-100" type="submit">Search</button></div>
        </div>
      </form>
      {loading ? <LoadingState label="Loading stations…" /> : error ? <ApiErrorState error={error} resourceName="stations" onRetry={() => { setLoading(true); setRetry((value) => value + 1) }} /> : results?.items.length ? (
        <>
          <div className="row g-3 mb-4">{results.items.map((station) => (
            <div className="col-12 col-md-6 col-xl-4" key={station.id}><StationSummaryCard station={station} /></div>
          ))}</div>
          <Pagination page={results.page} totalPages={results.totalPages} totalCount={results.totalCount} onPageChange={(next) => updateFilter('page', String(next))} />
        </>
      ) : <EmptyState title="No stations found" description="Try another search or status filter." />}
    </>
  )
}
