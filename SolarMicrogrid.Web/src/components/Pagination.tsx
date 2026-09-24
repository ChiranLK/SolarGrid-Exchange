interface PaginationProps {
  page: number
  totalPages: number
  totalCount: number
  onPageChange: (page: number) => void
}

export function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
}: PaginationProps) {
  if (totalPages <= 1) {
    return totalCount > 0 ? (
      <p className="small text-body-secondary mb-0" aria-live="polite">
        {totalCount} {totalCount === 1 ? 'result' : 'results'}
      </p>
    ) : null
  }

  return (
    <nav className="d-flex flex-column flex-sm-row align-items-sm-center justify-content-between gap-3" aria-label="Reservation pages">
      <p className="small text-body-secondary mb-0" aria-live="polite">
        Page {page} of {totalPages} · {totalCount} results
      </p>
      <div className="btn-group" role="group" aria-label="Pagination controls">
        <button
          type="button"
          className="btn btn-outline-secondary"
          disabled={page <= 1}
          onClick={() => onPageChange(page - 1)}
        >
          Previous
        </button>
        <button
          type="button"
          className="btn btn-outline-secondary"
          disabled={page >= totalPages}
          onClick={() => onPageChange(page + 1)}
        >
          Next
        </button>
      </div>
    </nav>
  )
}
