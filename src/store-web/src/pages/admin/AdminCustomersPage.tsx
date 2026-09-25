import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Badge, Button, Input, Select } from '../../ui/primitives'
import { FilterBar, PageHeader, Pager, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { formatDate, formatPrice } from '../../lib/format'
import type { Paged } from '../../lib/types'

interface CustomerRow {
  id: string
  userId: string
  fullName: string
  email: string
  phone?: string
  totalOrders: number
  totalSpent: number
  averageOrderValue: number
  lastOrderAt?: string
  isActive: boolean
  acceptsMarketing: boolean
  createdAt: string
}

export default function AdminCustomersPage() {
  const [search, setSearch] = useState('')
  const [sortBy, setSortBy] = useState('')
  const [page, setPage] = useState(1)

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'customers', { search, sortBy, page }],
    queryFn: () =>
      api.get<Paged<CustomerRow>>(
        `/admin/customers${qs({ search, sortBy: sortBy || undefined, page, pageSize: 20 })}`,
      ),
    placeholderData: keepPreviousData,
  })

  return (
    <div>
      <PageHeader title="Customers" description="Everyone who has registered an account." />

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
            placeholder="Name, email or phone…"
            className="h-9 w-72"
            aria-label="Search customers"
          />
          <Button type="submit" variant="outline" size="sm">
            Search
          </Button>
        </form>

        <Select
          value={sortBy}
          onChange={(event) => {
            setSortBy(event.target.value)
            setPage(1)
          }}
          className="h-9 w-48"
          aria-label="Sort customers"
        >
          <option value="">Newest first</option>
          <option value="spent">Highest spend</option>
          <option value="orders">Most orders</option>
          <option value="lastorder">Recently ordered</option>
        </Select>
      </FilterBar>

      {isLoading ? (
        <TableSkeleton columns={6} />
      ) : (
        <>
          <TableShell>
            <thead>
              <tr>
                <Th>Customer</Th>
                <Th align="right">Orders</Th>
                <Th align="right">Lifetime spend</Th>
                <Th align="right">Average order</Th>
                <Th>Last order</Th>
                <Th>Status</Th>
              </tr>
            </thead>
            <tbody>
              {data?.items.length === 0 && <TableEmpty colSpan={6} message="No customers match that search." />}

              {data?.items.map((customer) => (
                <tr key={customer.id} className="transition-colors hover:bg-ink-50/60">
                  <Td>
                    <div className="flex items-center gap-3">
                      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-saffron-100 text-xs font-bold text-saffron-700">
                        {customer.fullName.trim()[0]?.toUpperCase() ?? '?'}
                      </span>
                      <div className="min-w-0">
                        <span className="block truncate font-medium text-ink-800">{customer.fullName}</span>
                        <span className="block truncate text-[11px] text-ink-400">{customer.email}</span>
                      </div>
                    </div>
                  </Td>

                  <Td align="right" className="text-ink-700">
                    {customer.totalOrders}
                  </Td>

                  <Td align="right" className="font-semibold text-ink-900">
                    {formatPrice(customer.totalSpent)}
                  </Td>

                  <Td align="right" className="text-xs text-ink-500">
                    {formatPrice(customer.averageOrderValue)}
                  </Td>

                  <Td className="whitespace-nowrap text-xs text-ink-500">
                    {customer.lastOrderAt ? formatDate(customer.lastOrderAt) : '—'}
                  </Td>

                  <Td>
                    <div className="flex flex-wrap gap-1">
                      <Badge tone={customer.isActive ? 'success' : 'neutral'}>
                        {customer.isActive ? 'Active' : 'Deactivated'}
                      </Badge>
                      {customer.acceptsMarketing && <Badge tone="neutral">Marketing</Badge>}
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
