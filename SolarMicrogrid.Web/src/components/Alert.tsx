import type { ReactNode } from 'react'

export type AlertVariant = 'success' | 'danger' | 'warning' | 'info'

interface AlertProps {
  variant: AlertVariant
  children: ReactNode
  title?: string
  assertive?: boolean
  className?: string
}

export function Alert({
  variant,
  children,
  title,
  assertive = variant === 'danger',
  className = '',
}: AlertProps) {
  return (
    <div
      className={`alert alert-${variant} ${className}`.trim()}
      role={assertive ? 'alert' : 'status'}
      aria-live={assertive ? 'assertive' : 'polite'}
    >
      {title && <h2 className="h6 alert-heading">{title}</h2>}
      <div>{children}</div>
    </div>
  )
}
