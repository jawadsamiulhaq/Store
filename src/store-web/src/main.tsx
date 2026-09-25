import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router-dom'
import { router } from './app/router'
import { AuthProvider } from './app/providers/AuthProvider'
import { StoreProvider } from './app/providers/StoreProvider'
import { ApiError } from './lib/api'
import './styles/theme.css'

/**
 * Query client defaults.
 *
 * The important one is the retry rule: retrying a 4xx is pointless — a 404 will still be a 404,
 * and retrying a 401 fights the token-refresh logic. Only network and 5xx failures are worth a
 * second attempt.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 60_000,
      gcTime: 10 * 60 * 1000,

      // Refetching on every window focus is a lot of requests for a catalogue that barely
      // changes; the cart opts back in explicitly because it must be correct.
      refetchOnWindowFocus: false,

      retry: (failureCount, error) => {
        if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
          return false
        }

        return failureCount < 2
      },

      retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 8000),
    },
    mutations: {
      // A mutation is not safe to replay blindly — retrying a checkout could place two orders.
      retry: false,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <StoreProvider>
          <RouterProvider router={router} />
        </StoreProvider>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
