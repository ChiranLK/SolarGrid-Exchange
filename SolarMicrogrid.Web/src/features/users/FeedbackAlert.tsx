import type { ActionFeedback } from './userAdminModel'

interface FeedbackAlertProps {
  feedback: ActionFeedback | null
  onDismiss?: () => void
}

/** Success or failure message after an account action; announced to screen readers. */
export function FeedbackAlert({ feedback, onDismiss }: FeedbackAlertProps) {
  if (!feedback) return null
  const isSuccess = feedback.tone === 'success'

  return (
    <div
      className={`alert alert-${feedback.tone} d-flex align-items-start justify-content-between gap-3`}
      role={isSuccess ? 'status' : 'alert'}
      aria-live={isSuccess ? 'polite' : 'assertive'}
    >
      <span>{feedback.message}</span>
      {onDismiss && (
        <button type="button" className="btn-close" aria-label="Dismiss message" onClick={onDismiss} />
      )}
    </div>
  )
}
