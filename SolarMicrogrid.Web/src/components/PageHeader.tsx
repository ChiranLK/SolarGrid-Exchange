import type { ReactNode } from 'react'

interface PageHeaderProps {
  eyebrow?: string
  title: string
  description?: string
  actions?: ReactNode
}

export function PageHeader({ eyebrow, title, description, actions }: PageHeaderProps) {
  return (
    <header className="page-hero d-flex flex-column flex-lg-row align-items-lg-end justify-content-between gap-3 mb-4">
      <div className="page-hero-text">
        {eyebrow && <div className="page-eyebrow">{eyebrow}</div>}
        <h1 className="page-title">{title}</h1>
        {description && <p className="page-description mb-0">{description}</p>}
      </div>
      {actions && <div className="page-hero-actions d-flex flex-wrap gap-2">{actions}</div>}
    </header>
  )
}
