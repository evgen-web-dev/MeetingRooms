import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import AppLayout from './App.tsx'
import { AuthProvider } from './auth/AuthContext.tsx'
import { RequireAdmin, RequireAuth } from './auth/guards.tsx'
import AdminBookings from './pages/AdminBookings.tsx'
import Diagnostics from './pages/Diagnostics.tsx'
import Login from './pages/Login.tsx'
import MyBookings from './pages/MyBookings.tsx'
import Register from './pages/Register.tsx'
import RoomSchedule from './pages/RoomSchedule.tsx'
import Rooms from './pages/Rooms.tsx'
import './index.css'

/*
 * Real URLs rather than view state, which is why the API has always served index.html for an
 * unmatched route (`MapFallbackToFile`): a reviewer can link straight to a room's schedule and a
 * hard refresh lands on the same page.
 *
 * The guards wrap nested routes instead of each page checking for itself, so a new screen is
 * protected by where it is declared. Neither is a security boundary - every endpoint behind them
 * is gated server-side.
 */
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />

          <Route element={<RequireAuth />}>
            <Route element={<AppLayout />}>
              <Route path="/rooms" element={<Rooms />} />
              <Route path="/rooms/:roomId" element={<RoomSchedule />} />
              <Route path="/bookings" element={<MyBookings />} />
              <Route path="/diagnostics" element={<Diagnostics />} />

              <Route element={<RequireAdmin />}>
                <Route path="/admin/bookings" element={<AdminBookings />} />
              </Route>
            </Route>
          </Route>

          {/* Anything else, including "/", lands on the room list - or on the login screen, if the
              guard there sends it on. */}
          <Route path="*" element={<Navigate to="/rooms" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
