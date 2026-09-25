import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../app/providers/AuthProvider'
import type { WishlistItem } from '../../lib/types'

const WISHLIST_KEY = ['wishlist'] as const
const WISHLIST_IDS_KEY = ['wishlist', 'ids'] as const

export function useWishlist() {
  const { isAuthenticated } = useAuth()

  return useQuery({
    queryKey: WISHLIST_KEY,
    queryFn: () => api.get<WishlistItem[]>('/wishlist'),
    // Wishlist requires an account, so the request is not even attempted for a guest — that
    // would be a guaranteed 401 on every page load.
    enabled: isAuthenticated,
    staleTime: 60_000,
  })
}

/**
 * Just the saved product ids.
 *
 * One request per page lets every card in a 24-item grid render its heart in the right state.
 * Asking "is this saved?" per card would be 24 requests for information the server can return once.
 */
export function useWishlistIds() {
  const { isAuthenticated } = useAuth()

  const query = useQuery({
    queryKey: WISHLIST_IDS_KEY,
    queryFn: () => api.get<string[]>('/wishlist/ids'),
    enabled: isAuthenticated,
    staleTime: 60_000,
  })

  return {
    ...query,
    // A Set so the card's lookup is O(1) rather than a linear scan per card.
    ids: new Set(query.data ?? []),
  }
}

export function useWishlistToggle() {
  const queryClient = useQueryClient()
  const { isAuthenticated } = useAuth()

  return useMutation({
    mutationFn: async (productId: string) => {
      if (!isAuthenticated) {
        throw new Error('SIGN_IN_REQUIRED')
      }

      return api.post<boolean>(`/wishlist/${productId}/toggle`)
    },

    // Optimistic: the heart must fill the instant it is tapped.
    onMutate: async (productId) => {
      await queryClient.cancelQueries({ queryKey: WISHLIST_IDS_KEY })
      const previous = queryClient.getQueryData<string[]>(WISHLIST_IDS_KEY) ?? []

      queryClient.setQueryData<string[]>(
        WISHLIST_IDS_KEY,
        previous.includes(productId)
          ? previous.filter((id) => id !== productId)
          : [...previous, productId],
      )

      return { previous }
    },

    onError: (_error, _productId, context) => {
      if (context?.previous) {
        queryClient.setQueryData(WISHLIST_IDS_KEY, context.previous)
      }
    },

    // The full wishlist list is invalidated rather than patched, because its rows carry price and
    // stock the client cannot compute.
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: WISHLIST_KEY })
    },
  })
}

export function useWishlistRemove() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (productId: string) => api.del<void>(`/wishlist/${productId}`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: WISHLIST_KEY })
      void queryClient.invalidateQueries({ queryKey: WISHLIST_IDS_KEY })
    },
  })
}
