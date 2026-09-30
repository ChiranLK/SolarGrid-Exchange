import type { ReactNode } from 'react'
import { InboxIcon } from './icons'

interface EmptyStateProps {
  title: string
  description: string
  action?: ReactNode
  icon?: ReactNode
}

export function EmptyState({ title, description, action, icon }: EmptyStateProps) {
  return (
    <section className="state-panel text-center" aria-labelledby="empty-state-title">
      <div className="state-icon" aria-hidden="true">{icon ?? <InboxIcon width={28} height={28} />}</div>
      <h2 id="empty-state-title" className="h5 mb-2">{title}</h2>
      <p className="text-body-secondary mb-3">{description}</p>
      {action}
    </section>
  )
}
