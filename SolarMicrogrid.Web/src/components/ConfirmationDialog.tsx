import { useEffect, useRef } from 'react'

interface ConfirmationDialogProps {
  isOpen: boolean
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  isBusy?: boolean
  destructive?: boolean
  onConfirm: () => void
  onCancel: () => void
}

export function ConfirmationDialog({
  isOpen,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  isBusy = false,
  destructive = false,
  onConfirm,
  onCancel,
}: ConfirmationDialogProps) {
  const cancelButtonRef = useRef<HTMLButtonElement>(null)
  const dialogRef = useRef<HTMLElement>(null)
  const isBusyRef = useRef(isBusy)
  const onCancelRef = useRef(onCancel)

  useEffect(() => {
    isBusyRef.current = isBusy
    onCancelRef.current = onCancel
  }, [isBusy, onCancel])

  useEffect(() => {
    if (!isOpen) {
      return
    }

    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    cancelButtonRef.current?.focus()
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !isBusyRef.current) {
        onCancelRef.current()
      }
      if (event.key === 'Tab') {
        const focusable = dialogRef.current?.querySelectorAll<HTMLElement>(
          'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
        )
        if (!focusable?.length) return
        const first = focusable[0]
        const last = focusable[focusable.length - 1]
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault()
          last.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault()
          first.focus()
        }
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('keydown', handleKeyDown)
      if (previousFocus?.isConnected) previousFocus.focus()
    }
  }, [isOpen])

  if (!isOpen) {
    return null
  }

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={onCancel}>
      <section
        ref={dialogRef}
        className="card border-0 shadow-lg dialog-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirmation-title"
        aria-describedby="confirmation-message"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="card-body p-4">
          <h2 id="confirmation-title" className="h5">{title}</h2>
          <p id="confirmation-message" className="text-body-secondary mb-4">{message}</p>
          <div className="d-flex justify-content-end gap-2">
            <button
              ref={cancelButtonRef}
              type="button"
              className="btn btn-outline-secondary"
              disabled={isBusy}
              onClick={onCancel}
            >
              {cancelLabel}
            </button>
            <button
              type="button"
              className={`btn ${destructive ? 'btn-danger' : 'btn-success'}`}
              disabled={isBusy}
              onClick={onConfirm}
            >
              {isBusy ? 'Working…' : confirmLabel}
            </button>
          </div>
        </div>
      </section>
    </div>
  )
}
