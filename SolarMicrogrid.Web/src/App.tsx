import { Navigate, Route, Routes } from 'react-router-dom'
import { AppLayout } from './layouts/AppLayout'
import { ForbiddenPageWithNavigation } from './pages/ForbiddenPage'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { AboutPage } from './pages/public/AboutPage'
import { ContactPage } from './pages/public/ContactPage'
import { PublicLayout } from './pages/public/PublicLayout'
import { HomeGate } from './routes/HomeGate'
import { ProtectedRoute } from './routes/ProtectedRoute'
import { RoleRoute } from './routes/RoleRoute'
import { ReservationCreatePage } from './features/reservations/ReservationCreatePage'
import { ReservationDetailPage } from './features/reservations/ReservationDetailPage'
import { ReservationListPage } from './features/reservations/ReservationListPage'
import { BookingHistoryPage } from './features/operations/BookingHistoryPage'
import { OperatorDashboardPage } from './features/operations/OperatorDashboardPage'
import { operatorRoutes, operatorScreenRoles } from './features/operations/operatorDashboardModel'
import { StationDetailPage } from './features/stations/StationDetailPage'
import { StationFormPage } from './features/stations/StationFormPage'
import { StationListPage } from './features/stations/StationListPage'
import { StationSchedulePage } from './features/stations/StationSchedulePage'
import { SlotFormPage } from './features/stations/SlotFormPage'
import { CreateStaffPage } from './features/users/CreateStaffPage'
import { DeactivationRequestsPage } from './features/users/DeactivationRequestsPage'
import { PendingActivationsPage } from './features/users/PendingActivationsPage'
import { UserAdminLayout } from './features/users/UserAdminLayout'
import { UserManagementPage } from './features/users/UserManagementPage'
import { EditUserPage } from './features/users/EditUserPage'
import { ProsumerWebNoticePage } from './pages/ProsumerWebNoticePage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route element={<PublicLayout />}>
        <Route path="about" element={<AboutPage />} />
        <Route path="contact" element={<ContactPage />} />
      </Route>

      <Route element={<HomeGate />}>
        <Route index element={<HomePage />} />
      </Route>

      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route element={<RoleRoute allowedRoles={['Backoffice', 'GridOperator']} />}>
            <Route path="stations" element={<StationListPage />} />
            <Route path="stations/:stationId" element={<StationDetailPage />} />
            <Route path="reservations" element={<ReservationListPage />} />
            <Route path="reservations/new" element={<ReservationCreatePage />} />
            <Route path="reservations/:reservationId" element={<ReservationDetailPage />} />
            <Route path="operations" element={<Navigate to={`/${operatorRoutes.dashboard}`} replace />} />
          </Route>

          <Route element={<RoleRoute allowedRoles={operatorScreenRoles} />}>
            <Route path={operatorRoutes.dashboard} element={<OperatorDashboardPage />} />
            <Route path={operatorRoutes.history} element={<BookingHistoryPage />} />
            <Route path="dashboard" element={<Navigate to={`/${operatorRoutes.dashboard}`} replace />} />
          </Route>

          <Route element={<RoleRoute allowedRoles={['Prosumer']} />}>
            <Route path="prosumer" element={<ProsumerWebNoticePage />} />
          </Route>

          <Route element={<RoleRoute allowedRoles={['Backoffice']} />}>
            <Route path="users" element={<UserAdminLayout />}>
              <Route index element={<UserManagementPage />} />
              <Route path="new" element={<CreateStaffPage />} />
              <Route path=":nic/edit" element={<EditUserPage />} />
              <Route path="pending" element={<PendingActivationsPage />} />
              <Route path="deactivation-requests" element={<DeactivationRequestsPage />} />
            </Route>
            <Route path="stations/new" element={<StationFormPage />} />
            <Route path="stations/:stationId/edit" element={<StationFormPage />} />
            <Route path="stations/:stationId/schedule" element={<StationSchedulePage />} />
            <Route path="stations/:stationId/slots/new" element={<SlotFormPage />} />
            <Route path="stations/:stationId/slots/:slotId/edit" element={<SlotFormPage />} />
            <Route path="backoffice" element={<Navigate to="/users" replace />} />
          </Route>

          <Route path="forbidden" element={<ForbiddenPageWithNavigation />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Routes>
  )
}
