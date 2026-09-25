import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import type {
  Banner,
  Brand,
  ProductCard,
  ProductDetail,
  ProductReviews,
  ProductSearchResult,
  ProductSort,
} from '../../lib/types'

export interface CatalogFilters {
  search?: string
  category?: string
  brand?: string
  minPrice?: number
  maxPrice?: number
  inStock?: boolean
  onSale?: boolean
  minRating?: number
  tag?: string
  sort?: ProductSort
  page?: number
  pageSize?: number
}

export function useProducts(filters: CatalogFilters) {
  const page = filters.page ?? 1

  return useQuery({
    queryKey: ['products', filters],
    queryFn: () =>
      api.get<ProductSearchResult>(
        `/catalog/products${qs({
          ...filters,
          page,
          pageSize: filters.pageSize ?? 24,
          // Facets are computed from four extra aggregates, so they are requested only on the
          // first page — a shopper paging deeper already has them on screen.
          facets: page === 1,
        })}`,
      ),

    // Keeps the previous page visible while the next one loads. Without this the grid empties and
    // the page collapses to the header height, then springs back — a large, avoidable layout shift
    // on every pagination click.
    placeholderData: keepPreviousData,
    staleTime: 2 * 60 * 1000,
  })
}

export function useProduct(slug: string | undefined) {
  return useQuery({
    queryKey: ['product', slug],
    queryFn: () => api.get<ProductDetail>(`/catalog/products/${slug}`),
    enabled: Boolean(slug),
    staleTime: 5 * 60 * 1000,
  })
}

export function useRelatedProducts(productId: string | undefined) {
  return useQuery({
    queryKey: ['product', productId, 'related'],
    queryFn: () => api.get<ProductCard[]>(`/catalog/products/${productId}/related?take=8`),
    enabled: Boolean(productId),
    staleTime: 10 * 60 * 1000,
  })
}

export function useProductReviews(productId: string | undefined, page = 1) {
  return useQuery({
    queryKey: ['product', productId, 'reviews', page],
    queryFn: () => api.get<ProductReviews>(`/reviews/product/${productId}${qs({ page, pageSize: 10 })}`),
    enabled: Boolean(productId),
    staleTime: 2 * 60 * 1000,
  })
}

export function useBrands() {
  return useQuery({
    queryKey: ['brands'],
    queryFn: () => api.get<Brand[]>('/catalog/brands'),
    // Brands change about never, so this is held for the session.
    staleTime: 60 * 60 * 1000,
  })
}

export function useBanners(position = 0) {
  return useQuery({
    queryKey: ['banners', position],
    queryFn: () => api.get<Banner[]>(`/storefront/banners${qs({ position })}`),
    staleTime: 30 * 60 * 1000,
  })
}

/**
 * Home-page merchandising rails.
 *
 * Featured and trending are two filtered reads rather than one aggregated endpoint, because they
 * cache independently and either can be empty without affecting the other.
 */
export function useFeaturedProducts() {
  return useQuery({
    queryKey: ['products', 'featured'],
    queryFn: () =>
      api.get<ProductSearchResult>(
        `/catalog/products${qs({ sort: 'BestSelling', pageSize: 8, inStock: true })}`,
      ),
    staleTime: 10 * 60 * 1000,
  })
}

export function useNewArrivals() {
  return useQuery({
    queryKey: ['products', 'new'],
    queryFn: () =>
      api.get<ProductSearchResult>(`/catalog/products${qs({ sort: 'Newest', pageSize: 8 })}`),
    staleTime: 10 * 60 * 1000,
  })
}

export function useOnSaleProducts() {
  return useQuery({
    queryKey: ['products', 'sale'],
    queryFn: () =>
      api.get<ProductSearchResult>(
        `/catalog/products${qs({ onSale: true, pageSize: 8, inStock: true })}`,
      ),
    staleTime: 10 * 60 * 1000,
  })
}
