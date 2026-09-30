import { EmptyState } from '../components/EmptyState'
import { LockIcon } from '../components/icons'
import { PageHeader } from '../components/PageHeader'

interface FeaturePlaceholderPageProps {
  title: string
  description: string
  ownerNote: string
}

export function FeaturePlaceholderPage({
  title,
  description,
  ownerNote,
}: FeaturePlaceholderPageProps) {
  return (
    <>
      <PageHeader title={title} description={description} />
      <EmptyState title={`${title} workspace is ready`} description={ownerNote} icon={<LockIcon width={28} height={28} />} />
    </>
  )
}
