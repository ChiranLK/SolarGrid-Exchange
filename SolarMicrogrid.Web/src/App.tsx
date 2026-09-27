import { Route, Routes } from 'react-router-dom'
import { AppLayout } from './layouts/AppLayout'
import { DashboardPage } from './pages/DashboardPage'
import { FeaturePlaceholderPage } from './pages/FeaturePlaceholderPage'
import { ForbiddenPageWithNavigation } from './pages/ForbiddenPage'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { ProtectedRoute } from './routes/ProtectedRoute'
import { RoleRoute } from './routes/RoleRoute'
import { ReservationCreatePage } from './features/reservations/ReservationCreatePage'
import { ReservationDetailPage } from './features/reservations/ReservationDetailPage'
import { ReservationListPage } from './features/reservations/ReservationListPage'
import { StationDetailPage } from './features/stations/StationDetailPage'
import { StationFormPage } from './features/stations/StationFormPage'
import { StationListPage } from './features/stations/StationListPage'
import { StationSchedulePage } from './features/stations/StationSchedulePage'
import { SlotFormPage } from './features/stations/SlotFormPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route index element={<HomePage />} />
          <Route path="dashboard" element={<DashboardPage />} />
          <Route path="stations" element={<StationListPage />} />
          <Route path="stations/:stationId" element={<StationDetailPage />} />
          <Route path="reservations" element={<ReservationListPage />} />
          <Route path="reservations/new" element={<ReservationCreatePage />} />
          <Route path="reservations/:reservationId" element={<ReservationDetailPage />} />

          <Route element={<RoleRoute allowedRoles={['Backoffice', 'GridOperator']} />}>
            <Route
              path="users"
              element={(
                <FeaturePlaceholderPage
                  title="Users"
                  description="Staff-only route for account and Prosumer administration."
                  ownerNote="User API routes exist; feature-specific views remain with their owning team member."
                />
              )}
            />
            <Route
              path="operations"
              element={(
                <FeaturePlaceholderPage
                  title="Operations"
                  description="Staff-only route for grid operations workflows."
                  ownerNote="The Grid Operator workspace can be implemented here as backend contracts become available."
                />
              )}
            />
          </Route>

          <Route element={<RoleRoute allowedRoles={['Backoffice']} />}>
            <Route path="stations/new" element={<StationFormPage />} />
            <Route path="stations/:stationId/edit" element={<StationFormPage />} />
            <Route path="stations/:stationId/schedule" element={<StationSchedulePage />} />
            <Route path="stations/:stationId/slots/new" element={<SlotFormPage />} />
            <Route path="stations/:stationId/slots/:slotId/edit" element={<SlotFormPage />} />
            <Route
              path="backoffice"
              element={(
                <FeaturePlaceholderPage
                  title="Backoffice"
                  description="Backoffice-only administration route."
                  ownerNote="This route is protected by the API role convention and ready for administrative screens."
                />
              )}
            />
          </Route>

          <Route path="forbidden" element={<ForbiddenPageWithNavigation />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
