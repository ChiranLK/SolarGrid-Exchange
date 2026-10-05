import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router-dom'
import { userApi } from '../../api/userApi'
import { PageHeader } from '../../components/PageHeader'
import type { UserAdminContextValue } from './userAdminContext'

interface AdminCounts {
  pending: number | null
  deactivationRequests: number | null
}

const userAdminTabs = [
  { to: '/users', label: 'All users', end: true, countKey: null },
  { to: '/users/pending', label: 'Pending activation', end: false, countKey: 'pending' },
  { to: '/users/deactivation-requests', label: 'Deactivation requests', end: false, countKey: 'deactivationRequests' },
] as const

/** Backoffice account administration shell: shared header, tabs with live counts, refresh signal. */
export function UserAdminLayout() {
  const [version, setVersion] = useState(0)
  const [counts, setCounts] = useState<AdminCounts>({ pending: null, deactivationRequests: null })

  useEffect(() => {
    const controller = new AbortController()
    // Counts are a convenience; each tab shows its own loading and error state.
    Promise.allSettled([
      userApi.listPending(controller.signal),
      userApi.listDeactivationRequests(controller.signal),
    ]).then(([pending, requests]) => {
      if (controller.signal.aborted) return
      setCounts({
        pending: pending.status === 'fulfilled' ? pending.value.length : null,
        deactivationRequests: requests.status === 'fulfilled' ? requests.value.length : null,
      })
    })
    return () => controller.abort()
  }, [version])

  const notifyChanged = useCallback(() => setVersion((current) => current + 1), [])
  const context = useMemo<UserAdminContextValue>(() => ({ version, notifyChanged }), [notifyChanged, version])

  return (
    <>
      <PageHeader
        eyebrow="Backoffice"
        title="Account administration"
        description="Create staff accounts, approve new Prosumers, and manage account status."
        actions={<Link to="/users/new" className="btn btn-success">Create staff account</Link>}
      />
      <nav aria-label="Account administration sections" className="mb-4">
        <ul className="nav nav-tabs flex-nowrap overflow-auto">
          {userAdminTabs.map((tab) => {
            const count = tab.countKey ? counts[tab.countKey] : null
            return (
              <li className="nav-item" key={tab.to}>
                <NavLink to={tab.to} end={tab.end} className={({ isActive }) => `nav-link text-nowrap ${isActive ? 'active' : ''}`}>
                  {tab.label}
                  {count !== null && (
                    <span className="badge rounded-pill text-bg-secondary ms-2" aria-label={`${count} ${tab.label.toLowerCase()}`}>
                      {count}
                    </span>
                  )}
                </NavLink>
              </li>
            )
          })}
        </ul>
      </nav>
      <Outlet context={context} />
    </>
  )
}
