import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, setAccessToken } from '../../lib/api'
import type { AuthResponse, CurrentUser } from '../../lib/types'

interface AuthContextValue {
  user: CurrentUser | null
  isLoading: boolean
  isAuthenticated: boolean
  login: (email: string, password: string) => Promise<void>
  register: (input: RegisterInput) => Promise<void>
  logout: () => Promise<void>
  refreshUser: () => Promise<void>
  /** UI-level capability check. The server re-checks every gated action regardless. */
  can: (permission: string) => boolean
}

export interface RegisterInput {
  email: string
  password: string
  firstName: string
  lastName: string
  phone?: string
  acceptsMarketing: boolean
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  /**
   * Restores the session on first load by exchanging the HttpOnly refresh cookie for a fresh
   * access token. There is nothing in localStorage to read — that is deliberate — so this call
   * is the only way to know whether the visitor is signed in.
   */
  useEffect(() => {
    let cancelled = false

    void (async () => {
      try {
        const response = await api.post<AuthResponse>('/auth/refresh')

        if (!cancelled) {
          setAccessToken(response.accessToken)
          setUser(response.user)
        }
      } catch {
        // No cookie, or it expired. A signed-out visitor is the normal case, not an error.
        if (!cancelled) {
          setAccessToken(null)
          setUser(null)
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false)
        }
      }
    })()

    return () => {
      cancelled = true
    }
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const response = await api.post<AuthResponse>('/auth/login', { email, password })
    setAccessToken(response.accessToken)
    setUser(response.user)
  }, [])

  const register = useCallback(async (input: RegisterInput) => {
    const response = await api.post<AuthResponse>('/auth/register', input)
    setAccessToken(response.accessToken)
    setUser(response.user)
  }, [])

  const logout = useCallback(async () => {
    try {
      await api.post('/auth/logout')
    } finally {
      // Cleared even if the call fails — the local session must not survive a logout attempt.
      setAccessToken(null)
      setUser(null)
    }
  }, [])

  const refreshUser = useCallback(async () => {
    try {
      setUser(await api.get<CurrentUser>('/auth/me'))
    } catch {
      setAccessToken(null)
      setUser(null)
    }
  }, [])

  /**
   * Permission lookup backed by a Set.
   *
   * A System user short-circuits to true, matching the server. This gates UI only — it decides
   * what to *render*, never what is *allowed*. Every gated action is re-checked server-side, so
   * a tampered client gains nothing but a visible button that returns 403.
   */
  const permissionSet = useMemo(() => new Set(user?.permissions ?? []), [user])

  const can = useCallback(
    (permission: string) => (user?.isSystem ?? false) || permissionSet.has(permission),
    [user, permissionSet],
  )

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isLoading,
      isAuthenticated: user !== null,
      login,
      register,
      logout,
      refreshUser,
      can,
    }),
    [user, isLoading, login, register, logout, refreshUser, can],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)

  if (!context) {
    throw new Error('useAuth must be used inside <AuthProvider>.')
  }

  return context
}
