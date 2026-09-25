import { useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Image } from '../../ui/Image'
import { Badge, ButtonLink, Input, Select } from '../../ui/primitives'
import { FilterBar, PageHeader, Pager, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { formatPrice, formatPriceRange } from '../../lib/format'
import { useAuth } from '../../app/providers/AuthProvider'
import type { Paged } from '../../lib/types'

interface AdminProductRow {
  id: string
  name: string
  slug: string
  thumbnailUrl?: string
  categoryName?: string
  brandName?: string
  status: number
  minPrice: number
  maxPrice: number
  totalStock: number
  variantCount: number
  isFeatured: boolean
  isTrending: boolean
  hasLowStock: boolean
  createdAt: string
}

const STATUS_LABEL: Record<number, string> = { 0: 'Draft', 1: 'Active', 2: 'Archived' }

export default function AdminProductsPage() {
  const { can } = useAuth()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'products', { search, status, page }],
    queryFn: () =>
      api.get<Paged<AdminProductRow>>(
        `/admin/products${qs({ search, status: status || undefined, page, pageSize: 20 })}`,
      ),
    placeholderData: keepPreviousData,
  })

  return (
    <div>
      <PageHeader
        title="Products"
        description="The catalogue. Price and stock live on each product's variants."
        action={
          can('products.create') ? <ButtonLink to="/admin/products/new">New product</ButtonLink> : undefined
        }
      />

      <FilterBar>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            setPage(1)
          }}
          className="flex gap-2"
        >
          <Input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Name, SKU or barcode…"
            className="h-9 w-72"
            aria-label="Search products"
          />
          <button
            type="submit"
            className="h-9 rounded-lg border border-ink-200 bg-paper-raised px-3 text-sm text-ink-700 hover:border-ink-300"
          >
            Search
          </button>
        </form>

        <Select
          value={status}
          onChange={(event) => {
            setStatus(event.target.value)
            setPage(1)
          }}
          className="h-9 w-40"
          aria-label="Filter by status"
        >
          <option value="">All statuses</option>
          <option value="1">Active</option>
          <option value="0">Draft</option>
          <option value="2">Archived</option>
        </Select>
      </FilterBar>

      {isLoading ? (
        <TableSkeleton columns={6} />
      ) : (
        <>
          <TableShell>
            <thead>
              <tr>
                <Th>Product</Th>
                <Th>Category</Th>
                <Th align="right">Price</Th>
                <Th align="right">Stock</Th>
                <Th>Status</Th>
                <Th align="right"> </Th>
              </tr>
            </thead>
            <tbody>
              {data?.items.length === 0 && <TableEmpty colSpan={6} message="No products match those filters." />}

              {data?.items.map((product) => (
                <tr key={product.id} className="transition-colors hover:bg-ink-50/60">
                  <Td>
                    <div className="flex items-center gap-3">
                      <Image
                        src={product.thumbnailUrl}
                        alt=""
                        width={40}
                        height={40}
                        className="h-10 w-10 shrink-0 rounded-lg"
                      />
                      <div className="min-w-0">
                        <span className="block truncate font-medium text-ink-800">{product.name}</span>
                        <span className="block truncate text-[11px] text-ink-400">
                          {product.brandName ?? 'No brand'} · {product.variantCount} variant
                          {product.variantCount === 1 ? '' : 's'}
                        </span>
                      </div>
                    </div>
                  </Td>

                  <Td className="text-xs text-ink-500">{product.categoryName ?? '—'}</Td>

                  <Td align="right" className="whitespace-nowrap font-medium text-ink-800">
                    {product.minPrice === product.maxPrice
                      ? formatPrice(product.minPrice)
                      : formatPriceRange(product.minPrice, product.maxPrice)}
                  </Td>

                  <Td align="right">
                    <span
                      className={`font-medium ${
                        product.totalStock === 0
                          ? 'text-chilli-600'
                          : product.hasLowStock
                            ? 'text-saffron-600'
                            : 'text-ink-700'
                      }`}
                    >
                      {product.totalStock}
                    </span>
                    {product.hasLowStock && product.totalStock > 0 && (
                      <span className="block text-[10px] text-saffron-600">low</span>
                    )}
                  </Td>

                  <Td>
                    <div className="flex flex-wrap gap-1">
                      <Badge tone={product.status === 1 ? 'success' : 'neutral'}>
                        {STATUS_LABEL[product.status]}
                      </Badge>
                      {product.isFeatured && <Badge tone="new">Featured</Badge>}
                    </div>
                  </Td>

                  <Td align="right">
                    <div className="flex items-center justify-end gap-1">
                      {can('products.update') && (
                        <Link
                          to={`/admin/products/${product.id}/edit`}
                          className="rounded-lg px-2 py-1 text-xs font-medium text-saffron-600 transition-colors hover:bg-ink-100 hover:text-saffron-700"
                        >
                          Edit
                        </Link>
                      )}

                      {/* Opens the public page so staff can verify what a shopper actually sees. */}
                      <Link
                        to={`/product/${product.slug}`}
                        target="_blank"
                        rel="noopener"
                        className="rounded-lg px-2 py-1 text-xs font-medium text-ink-500 transition-colors hover:bg-ink-100 hover:text-ink-700"
                      >
                        View ↗
                      </Link>
                    </div>
                  </Td>
                </tr>
              ))}
            </tbody>
          </TableShell>

          {data && (
            <Pager page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} />
          )}
        </>
      )}
    </div>
  )
}
