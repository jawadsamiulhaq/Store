import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../providers/AuthProvider'
import { RouteFallback } from './RouteFallback'

/*
  Route guards.

  These decide what to *render*. They are not a security boundary — every protected endpoint
  re-checks authentication and permissions server-side, so a user who edits their way past one of
  these guards reaches a page whose API calls all return 401 or 403. The guards exist so that
  does not happen by accident to an honest user.
*/

/** Requires a signed-in user. Remembers where they were headed so login can return them. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth()
  const location = useLocation()

  // The session is restored asynchronously on first load. Redirecting before that resolves would
  // bounce a signed-in user to the login page on every refresh.
  if (isLoading) {
    return <RouteFallback />
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location.pathname + location.search }} replace />
  }

  return <>{children}</>
}

/** Requires staff (System or Admin) to reach the admin area at all. */
export function RequireStaff({ children }: { children: ReactNode }) {
  const { user, isLoading, isAuthenticated } = useAuth()
  const location = useLocation()

  if (isLoading) {
    return <RouteFallback />
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location.pathname }} replace />
  }

  // A signed-in customer who lands on /admin is sent home rather than shown a permission error —
  // the admin area should not even acknowledge itself to someone with no business there.
  if (!user?.isStaff) {
    return <Navigate to="/" replace />
  }

  return <>{children}</>
}

/**
 * Renders children only when the user holds a permission.
 *
 * Used to hide buttons and nav entries a user cannot act on. Hiding a control is a usability
 * choice, not a protection: the endpoint behind it enforces the same permission.
 */
export function Can({
  permission,
  fallback = null,
  children,
}: {
  permission: string
  fallback?: ReactNode
  children: ReactNode
}) {
  const { can } = useAuth()
  return can(permission) ? <>{children}</> : <>{fallback}</>
}
