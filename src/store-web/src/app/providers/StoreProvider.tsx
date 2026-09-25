import { useQuery } from '@tanstack/react-query'
import { createContext, useContext, useMemo, type ReactNode } from 'react'
import { api } from '../../lib/api'
import type { CategoryTree, ContentPage, StorefrontBootstrap } from '../../lib/types'

interface StoreContextValue {
  settings: Record<string, string>
  categories: CategoryTree[]
  footerPages: ContentPage[]
  isLoading: boolean
  /** Reads a store setting with a fallback, so a missing row never blanks the UI. */
  setting: (key: string, fallback?: string) => string
  settingNumber: (key: string, fallback: number) => number
  settingBool: (key: string, fallback: boolean) => boolean
}

const StoreContext = createContext<StoreContextValue | null>(null)

/**
 * Loads the storefront shell.
 *
 * One request — `/api/storefront/bootstrap` — returns the public settings, the category tree and
 * the footer pages together. Three separate calls would each add a round trip to the critical
 * path before the header could render, and "minimal API requests" is part of the performance
 * budget rather than a nicety.
 *
 * The payload is output-cached server-side and held for an hour client-side, because none of it
 * changes between page views.
 */
export function StoreProvider({ children }: { children: ReactNode }) {
  const { data, isLoading } = useQuery({
    queryKey: ['storefront', 'bootstrap'],
    queryFn: () => api.get<StorefrontBootstrap>('/storefront/bootstrap'),
    staleTime: 60 * 60 * 1000,
    gcTime: 2 * 60 * 60 * 1000,
    // Navigation must never re-block on this.
    refetchOnWindowFocus: false,
    refetchOnMount: false,
  })

  const value = useMemo<StoreContextValue>(() => {
    const settings = data?.settings ?? {}

    const setting = (key: string, fallback = '') => settings[key] ?? fallback

    return {
      settings,
      categories: data?.categories ?? [],
      footerPages: data?.footerPages ?? [],
      isLoading,
      setting,
      settingNumber: (key, fallback) => {
        const parsed = Number(settings[key])
        return Number.isFinite(parsed) ? parsed : fallback
      },
      settingBool: (key, fallback) => {
        const raw = settings[key]
        return raw === undefined ? fallback : raw === 'true'
      },
    }
  }, [data, isLoading])

  return <StoreContext value={value}>{children}</StoreContext>
}

export function useStore(): StoreContextValue {
  const context = useContext(StoreContext)

  if (!context) {
    throw new Error('useStore must be used inside <StoreProvider>.')
  }

  return context
}
