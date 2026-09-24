import { EmptyState } from '../components/EmptyState'
import { PageHeader } from '../components/PageHeader'

export function DashboardPage() {
  return (
    <>
      <PageHeader
        eyebrow="Overview"
        title="Dashboard"
        description="A shared route ready for the group dashboard once its API contract is implemented."
      />
      <EmptyState
        title="Dashboard integration is pending"
        description="DashboardController is currently empty, so this page deliberately makes no backend request and shows no invented metrics."
      />
    </>
  )
}
