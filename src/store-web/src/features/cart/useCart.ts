import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { Cart } from '../../lib/types'

const CART_KEY = ['cart'] as const

/**
 * The cart lives in TanStack Query, not in a client store.
 *
 * The server is the authority on price, stock and discount, and it recalculates all three on
 * every mutation. Mirroring that into Zustand would mean maintaining a second copy that is
 * wrong the moment a price changes or stock runs out — so every mutation simply writes the
 * server's response straight into the cache.
 */
export function useCart() {
  return useQuery({
    queryKey: CART_KEY,
    queryFn: () => api.get<Cart>('/cart'),
    // The cart is small and correctness matters more than saving a request, so it is refetched
    // when the tab regains focus (the shopper may have changed it in another tab).
    staleTime: 30_000,
    refetchOnWindowFocus: true,
  })
}

export function useCartMutations() {
  const queryClient = useQueryClient()

  // Every mutation returns the recalculated cart, so the cache is seeded from the response
  // rather than invalidated — one round trip instead of two.
  const write = (cart: Cart) => queryClient.setQueryData(CART_KEY, cart)

  const addItem = useMutation({
    mutationFn: (input: { productVariantId: string; quantity: number }) =>
      api.post<Cart>('/cart/items', input),
    onSuccess: write,
  })

  const updateQuantity = useMutation({
    mutationFn: ({ itemId, quantity }: { itemId: string; quantity: number }) =>
      api.put<Cart>(`/cart/items/${itemId}`, { quantity }),

    // Optimistic: quantity steppers must feel instant. The server response overwrites this,
    // and a failure rolls back to the snapshot.
    onMutate: async ({ itemId, quantity }) => {
      await queryClient.cancelQueries({ queryKey: CART_KEY })
      const previous = queryClient.getQueryData<Cart>(CART_KEY)

      if (previous) {
        queryClient.setQueryData<Cart>(CART_KEY, {
          ...previous,
          items: previous.items.map((item) =>
            item.id === itemId
              ? { ...item, quantity, lineTotal: item.unitPrice * quantity }
              : item,
          ),
        })
      }

      return { previous }
    },
    onError: (_error, _input, context) => {
      if (context?.previous) {
        queryClient.setQueryData(CART_KEY, context.previous)
      }
    },
    onSuccess: write,
  })

  const removeItem = useMutation({
    mutationFn: (itemId: string) => api.del<Cart>(`/cart/items/${itemId}`),
    onMutate: async (itemId) => {
      await queryClient.cancelQueries({ queryKey: CART_KEY })
      const previous = queryClient.getQueryData<Cart>(CART_KEY)

      if (previous) {
        queryClient.setQueryData<Cart>(CART_KEY, {
          ...previous,
          items: previous.items.filter((item) => item.id !== itemId),
        })
      }

      return { previous }
    },
    onError: (_error, _input, context) => {
      if (context?.previous) {
        queryClient.setQueryData(CART_KEY, context.previous)
      }
    },
    onSuccess: write,
  })

  const clear = useMutation({
    mutationFn: () => api.del<Cart>('/cart'),
    onSuccess: write,
  })

  const applyCoupon = useMutation({
    mutationFn: (code: string) => api.post<Cart>('/cart/coupon', { code }),
    onSuccess: write,
  })

  const removeCoupon = useMutation({
    mutationFn: () => api.del<Cart>('/cart/coupon'),
    onSuccess: write,
  })

  return { addItem, updateQuantity, removeItem, clear, applyCoupon, removeCoupon }
}

/** Badge count for the header. Reads the cached cart, so it costs no extra request. */
export function useCartCount(): number {
  const { data } = useCart()
  return data?.totals.itemCount ?? 0
}
