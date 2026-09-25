import type { ReactNode } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
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
 * Gates a whole admin route on one permission.
 *
 * `RequireStaff` only establishes that someone belongs in the admin area at all. Without this,
 * a staff member with, say, only `orders.view` could still open the product editor and the user
 * matrix — the pages would render, every request inside them would come back 403, and the result
 * reads as a broken application rather than as a boundary.
 *
 * Like the other guards this decides what to *render*. The endpoints behind each page check the
 * same permission again, which is where the actual enforcement lives.
 */
export function RequirePermission({
  permission,
  children,
}: {
  permission: string
  children: ReactNode
}) {
  const { can, isLoading } = useAuth()

  // The permission set arrives with the session, so deciding before it resolves would bounce
  // every reload off the user's own pages.
  if (isLoading) {
    return <RouteFallback />
  }

  if (!can(permission)) {
    return <Forbidden permission={permission} />
  }

  return <>{children}</>
}

/**
 * Shown in place of a page the signed-in user may not open.
 *
 * Deliberately not a redirect: bouncing someone to the dashboard from a link a colleague sent
 * them looks like the link is broken. Naming the missing permission is what lets them ask for
 * the right thing.
 */
function Forbidden({ permission }: { permission: string }) {
  return (
    <div className="mx-auto max-w-md py-16 text-center">
      <h1 className="font-display text-xl font-bold text-ink-900">You don’t have access to this</h1>

      <p className="mt-2 text-sm leading-relaxed text-ink-500">
        This page needs the{' '}
        <code className="rounded bg-ink-100 px-1.5 py-0.5 font-mono text-xs text-ink-700">
          {permission}
        </code>{' '}
        permission. Ask someone who can administer roles to grant it.
      </p>

      <Link
        to="/admin"
        className="mt-5 inline-block text-sm font-medium text-saffron-600 hover:text-saffron-700"
      >
        Back to the dashboard
      </Link>
    </div>
  )
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
