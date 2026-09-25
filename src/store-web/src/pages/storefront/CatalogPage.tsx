import { useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ProductCard, ProductCardSkeleton } from '../../features/catalog/ProductCard'
import { useProducts, type CatalogFilters } from '../../features/catalog/useCatalog'
import { useWishlistIds } from '../../features/wishlist/useWishlist'
import { useStore } from '../../app/providers/StoreProvider'
import { Button, EmptyState, Select } from '../../ui/primitives'
import { formatPrice } from '../../lib/format'
import type { ProductSort } from '../../lib/types'

const SORT_OPTIONS: { value: ProductSort; label: string }[] = [
  { value: 'Relevance', label: 'Most relevant' },
  { value: 'Newest', label: 'Newest first' },
  { value: 'PriceLowToHigh', label: 'Price: low to high' },
  { value: 'PriceHighToLow', label: 'Price: high to low' },
  { value: 'TopRated', label: 'Best rated' },
  { value: 'BestSelling', label: 'Best selling' },
  { value: 'NameAToZ', label: 'Name: A to Z' },
]

/**
 * The catalogue.
 *
 * **All filter state lives in the URL**, never in component state. That is what makes a filtered
 * view shareable, bookmarkable and correct under the back button — and it means the query key is
 * derived from the URL, so navigation history doubles as a result cache.
 */
export default function CatalogPage() {
  const { slug } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const { categories } = useStore()
  const { ids: savedIds } = useWishlistIds()
  const [filtersOpen, setFiltersOpen] = useState(false)

  // Route-derived category/brand (from /category/:slug) beats an explicit query parameter.
  const routeCategory = window.location.pathname.startsWith('/category/') ? slug : undefined
  const routeBrand = window.location.pathname.startsWith('/brand/') ? slug : undefined

  const filters = useMemo<CatalogFilters>(
    () => ({
      search: searchParams.get('search') ?? undefined,
      category: routeCategory ?? searchParams.get('category') ?? undefined,
      brand: routeBrand ?? searchParams.get('brand') ?? undefined,
      minPrice: numberParam(searchParams.get('minPrice')),
      maxPrice: numberParam(searchParams.get('maxPrice')),
      inStock: searchParams.get('inStock') === 'true' ? true : undefined,
      onSale: searchParams.get('onSale') === 'true' ? true : undefined,
      minRating: numberParam(searchParams.get('minRating')),
      sort: (searchParams.get('sort') as ProductSort | null) ?? 'Relevance',
      page: numberParam(searchParams.get('page')) ?? 1,
    }),
    [searchParams, routeCategory, routeBrand],
  )

  const { data, isLoading, isPlaceholderData } = useProducts(filters)

  const products = data?.products.items ?? []
  const facets = data?.facets
  const totalCount = data?.products.totalCount ?? 0
  const totalPages = data?.products.totalPages ?? 0
  const page = filters.page ?? 1

  /** Writes one filter to the URL, resetting to page 1 because the old page may not exist. */
  function setFilter(key: string, value: string | undefined) {
    const next = new URLSearchParams(searchParams)

    if (value === undefined || value === '') {
      next.delete(key)
    } else {
      next.set(key, value)
    }

    if (key !== 'page') {
      next.delete('page')
    }

    setSearchParams(next, { preventScrollReset: key !== 'page' })
  }

  function clearFilters() {
    const next = new URLSearchParams()
    const search = searchParams.get('search')
    if (search) next.set('search', search)
    setSearchParams(next)
  }

  const activeCategory = categories
    .flatMap((category) => [category, ...category.children])
    .find((category) => category.slug === filters.category)

  const hasActiveFilters =
    filters.minPrice !== undefined ||
    filters.maxPrice !== undefined ||
    filters.inStock !== undefined ||
    filters.onSale !== undefined ||
    filters.minRating !== undefined ||
    filters.brand !== undefined

  const heading = filters.search
    ? `Results for “${filters.search}”`
    : (activeCategory?.name ?? (filters.brand ? `Brand: ${filters.brand}` : 'All products'))

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <nav aria-label="Breadcrumb" className="text-xs text-ink-400">
        <Link to="/" className="hover:text-saffron-600">
          Home
        </Link>
        <span className="mx-1.5">/</span>
        <span className="text-ink-600">{heading}</span>
      </nav>

      <div className="mt-3 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-ink-900 sm:text-3xl">{heading}</h1>
          {/*
            The count area always occupies a line — even while loading — so the grid below does
            not jump upward when the number arrives.
          */}
          <p className="mt-1 min-h-5 text-sm text-ink-500">
            {isLoading ? 'Loading…' : `${totalCount.toLocaleString('en-HK')} products`}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => setFiltersOpen((open) => !open)} className="lg:hidden">
            Filters{hasActiveFilters ? ' •' : ''}
          </Button>

          <label htmlFor="sort" className="sr-only">
            Sort products
          </label>
          <Select
            id="sort"
            value={filters.sort}
            onChange={(event) => setFilter('sort', event.target.value)}
            className="h-9 w-48 text-sm"
          >
            {SORT_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </Select>
        </div>
      </div>

      <div className="mt-6 flex gap-8">
        <aside
          className={`${filtersOpen ? 'block' : 'hidden'} w-full shrink-0 lg:block lg:w-64`}
          aria-label="Filters"
        >
          <div className="space-y-6 lg:sticky lg:top-36">
            {hasActiveFilters && (
              <button
                type="button"
                onClick={clearFilters}
                className="text-xs font-medium text-chilli-600 hover:text-chilli-700"
              >
                Clear all filters
              </button>
            )}

            <FilterGroup title="Availability">
              <Checkbox
                label="In stock only"
                count={facets?.inStockCount}
                checked={filters.inStock === true}
                onChange={(checked) => setFilter('inStock', checked ? 'true' : undefined)}
              />
              <Checkbox
                label="On offer"
                count={facets?.onSaleCount}
                checked={filters.onSale === true}
                onChange={(checked) => setFilter('onSale', checked ? 'true' : undefined)}
              />
            </FilterGroup>

            <FilterGroup title="Price">
              <div className="flex items-center gap-2">
                <input
                  type="number"
                  inputMode="numeric"
                  min={0}
                  placeholder={facets ? String(Math.floor(facets.minPrice)) : 'Min'}
                  defaultValue={filters.minPrice ?? ''}
                  onBlur={(event) => setFilter('minPrice', event.target.value || undefined)}
                  aria-label="Minimum price"
                  className="h-9 w-full rounded-lg border border-ink-200 bg-paper-raised px-2 text-sm"
                />
                <span className="text-ink-300">–</span>
                <input
                  type="number"
                  inputMode="numeric"
                  min={0}
                  placeholder={facets ? String(Math.ceil(facets.maxPrice)) : 'Max'}
                  defaultValue={filters.maxPrice ?? ''}
                  onBlur={(event) => setFilter('maxPrice', event.target.value || undefined)}
                  aria-label="Maximum price"
                  className="h-9 w-full rounded-lg border border-ink-200 bg-paper-raised px-2 text-sm"
                />
              </div>
              {facets && (
                <p className="mt-2 text-xs text-ink-400">
                  {formatPrice(facets.minPrice)} – {formatPrice(facets.maxPrice)} in these results
                </p>
              )}
            </FilterGroup>

            <FilterGroup title="Rating">
              {[4, 3, 2].map((rating) => (
                <Checkbox
                  key={rating}
                  label={`${rating} stars & up`}
                  checked={filters.minRating === rating}
                  onChange={(checked) => setFilter('minRating', checked ? String(rating) : undefined)}
                />
              ))}
            </FilterGroup>

            {/* Facet counts come from the same filtered query as the results, so a filter shown
                here can never return zero products. */}
            {facets && facets.categories.length > 1 && (
              <FilterGroup title="Category">
                {facets.categories.slice(0, 10).map((facet) => (
                  <Link
                    key={facet.id}
                    to={`/category/${facet.slug}`}
                    className="flex items-center justify-between py-1 text-sm text-ink-600 hover:text-saffron-600"
                  >
                    <span className="truncate">{facet.name}</span>
                    <span className="ml-2 shrink-0 text-xs text-ink-400">{facet.count}</span>
                  </Link>
                ))}
              </FilterGroup>
            )}

            {facets && facets.brands.length > 1 && (
              <FilterGroup title="Brand">
                {facets.brands.slice(0, 10).map((facet) => (
                  <Checkbox
                    key={facet.id}
                    label={facet.name}
                    count={facet.count}
                    checked={filters.brand === facet.slug}
                    onChange={(checked) => setFilter('brand', checked ? facet.slug : undefined)}
                  />
                ))}
              </FilterGroup>
            )}
          </div>
        </aside>

        <div className="min-w-0 flex-1">
          {isLoading ? (
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 xl:grid-cols-4">
              {Array.from({ length: 12 }, (_, index) => (
                <ProductCardSkeleton key={index} />
              ))}
            </div>
          ) : products.length === 0 ? (
            <EmptyState
              title="Nothing matched those filters"
              description="Try widening the price range, or clearing a filter or two."
              action={
                <Button variant="outline" onClick={clearFilters}>
                  Clear filters
                </Button>
              }
            />
          ) : (
            <>
              {/*
                Dimmed while the next page loads, via opacity only. The previous page stays mounted
                (keepPreviousData), so nothing collapses and the scroll position holds.
              */}
              <div
                className={`grid grid-cols-2 gap-4 transition-opacity duration-200 sm:grid-cols-3 xl:grid-cols-4 ${
                  isPlaceholderData ? 'opacity-60' : 'opacity-100'
                }`}
              >
                {products.map((product, index) => (
                  <ProductCard
                    key={product.id}
                    product={product}
                    priority={page === 1 && index < 4}
                    isSaved={savedIds.has(product.id)}
                  />
                ))}
              </div>

              {totalPages > 1 && (
                <Pagination
                  page={page}
                  totalPages={totalPages}
                  onChange={(next) => setFilter('page', String(next))}
                />
              )}
            </>
          )}
        </div>
      </div>
    </div>
  )
}

function FilterGroup({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <fieldset>
      <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-ink-500">{title}</legend>
      <div className="space-y-1">{children}</div>
    </fieldset>
  )
}

function Checkbox({
  label,
  count,
  checked,
  onChange,
}: {
  label: string
  count?: number
  checked: boolean
  onChange: (checked: boolean) => void
}) {
  return (
    <label className="flex cursor-pointer items-center justify-between gap-2 py-1 text-sm text-ink-600">
      <span className="flex items-center gap-2">
        <input
          type="checkbox"
          checked={checked}
          onChange={(event) => onChange(event.target.checked)}
          className="h-4 w-4 rounded border-ink-300 text-saffron-500 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-saffron-500"
        />
        <span className="truncate">{label}</span>
      </span>
      {count !== undefined && <span className="shrink-0 text-xs text-ink-400">{count}</span>}
    </label>
  )
}

/**
 * Pagination with elision.
 *
 * At 175 pages, rendering every number is unusable. This shows the first, last, current and its
 * neighbours — so the control stays a fixed width no matter how deep the catalogue goes.
 */
function Pagination({
  page,
  totalPages,
  onChange,
}: {
  page: number
  totalPages: number
  onChange: (page: number) => void
}) {
  const pages = useMemo(() => {
    const result: (number | 'gap')[] = []
    const window = 1

    for (let candidate = 1; candidate <= totalPages; candidate++) {
      const isEdge = candidate === 1 || candidate === totalPages
      const isNear = Math.abs(candidate - page) <= window

      if (isEdge || isNear) {
        result.push(candidate)
      } else if (result.at(-1) !== 'gap') {
        result.push('gap')
      }
    }

    return result
  }, [page, totalPages])

  return (
    <nav className="mt-10 flex items-center justify-center gap-1" aria-label="Pagination">
      <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onChange(page - 1)}>
        Previous
      </Button>

      <div className="mx-2 flex items-center gap-1">
        {pages.map((entry, index) =>
          entry === 'gap' ? (
            <span key={`gap-${index}`} className="px-1 text-ink-300" aria-hidden="true">
              …
            </span>
          ) : (
            <button
              key={entry}
              type="button"
              onClick={() => onChange(entry)}
              aria-current={entry === page ? 'page' : undefined}
              className={`h-9 min-w-9 rounded-lg px-2 text-sm font-medium transition-colors ${
                entry === page
                  ? 'bg-saffron-500 text-white'
                  : 'text-ink-600 hover:bg-ink-100'
              }`}
            >
              {entry}
            </button>
          ),
        )}
      </div>

      <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
        Next
      </Button>
    </nav>
  )
}

function numberParam(value: string | null): number | undefined {
  if (value === null || value === '') return undefined
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
