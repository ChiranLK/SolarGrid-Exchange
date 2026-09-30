import { useAuth } from '../auth/useAuth'
import { LoadingState } from '../components/LoadingState'
import { AppLayout } from '../layouts/AppLayout'
import { LandingPage } from '../pages/public/LandingPage'
import { PublicLayout } from '../pages/public/PublicLayout'

/** "/" shows the public home page to visitors and the signed-in workspace to members. */
export function HomeGate() {
  const { isAuthenticated, isInitializing } = useAuth()

  if (isInitializing) {
    return <LoadingState label="Restoring your session…" />
  }

  if (!isAuthenticated) {
    return <PublicLayout><LandingPage /></PublicLayout>
  }

  return <AppLayout />
}
