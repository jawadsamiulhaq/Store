import { useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Alert, Badge, Button, Field, Input, Select } from '../../ui/primitives'
import { FilterBar, PageHeader, Pager, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { useAuth } from '../../app/providers/AuthProvider'
import { formatDate, formatPrice } from '../../lib/format'
import type { Paged } from '../../lib/types'

interface Coupon {
  id: string
  code: string
  description?: string
  discountType: number
  value: number
  minOrderAmount?: number
  maxDiscountAmount?: number
  scope: number
  usageLimit?: number
  usageLimitPerCustomer?: number
  usedCount: number
  firstOrderOnly: boolean
  startsAt?: string
  endsAt?: string
  isActive: boolean
  isCurrentlyValid: boolean
}

const BLANK = {
  code: '',
  description: '',
  discountType: 0,
  value: 10,
  minOrderAmount: '',
  maxDiscountAmount: '',
  usageLimit: '',
  usageLimitPerCustomer: '',
  firstOrderOnly: false,
  endsAt: '',
  isActive: true,
}

export default function AdminCouponsPage() {
  const queryClient = useQueryClient()
  const { can } = useAuth()

  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<Coupon | 'new' | null>(null)
  const [form, setForm] = useState(BLANK)

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'coupons', { search, page }],
    queryFn: () => api.get<Paged<Coupon>>(`/admin/coupons${qs({ search, page, pageSize: 20 })}`),
    placeholderData: keepPreviousData,
  })

  const save = useMutation({
    mutationFn: () => {
      const payload = {
        code: form.code.trim(),
        description: form.description.trim() || null,
        discountType: Number(form.discountType),
        value: Number(form.value),
        minOrderAmount: form.minOrderAmount ? Number(form.minOrderAmount) : null,
        maxDiscountAmount: form.maxDiscountAmount ? Number(form.maxDiscountAmount) : null,
        scope: 0,
        usageLimit: form.usageLimit ? Number(form.usageLimit) : null,
        usageLimitPerCustomer: form.usageLimitPerCustomer ? Number(form.usageLimitPerCustomer) : null,
        firstOrderOnly: form.firstOrderOnly,
        startsAt: null,
        endsAt: form.endsAt ? new Date(form.endsAt).toISOString() : null,
        isActive: form.isActive,
      }

      return editing === 'new'
        ? api.post<Coupon>('/admin/coupons', payload)
        : api.put<Coupon>(`/admin/coupons/${(editing as Coupon).id}`, payload)
    },
    onSuccess: async () => {
      setEditing(null)
      setForm(BLANK)
      await queryClient.invalidateQueries({ queryKey: ['admin', 'coupons'] })
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.del<void>(`/admin/coupons/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'coupons'] }),
  })

  function startEdit(coupon: Coupon) {
    setForm({
      code: coupon.code,
      description: coupon.description ?? '',
      discountType: coupon.discountType,
      value: coupon.value,
      minOrderAmount: coupon.minOrderAmount?.toString() ?? '',
      maxDiscountAmount: coupon.maxDiscountAmount?.toString() ?? '',
      usageLimit: coupon.usageLimit?.toString() ?? '',
      usageLimitPerCustomer: coupon.usageLimitPerCustomer?.toString() ?? '',
      firstOrderOnly: coupon.firstOrderOnly,
      endsAt: coupon.endsAt ? coupon.endsAt.slice(0, 10) : '',
      isActive: coupon.isActive,
    })
    setEditing(coupon)
  }

  return (
    <div>
      <PageHeader
        title="Coupons"
        description="Discount codes. Every limit is enforced again at checkout."
        action={
          can('coupons.create') && editing === null ? (
            <Button
              onClick={() => {
                setForm(BLANK)
                setEditing('new')
              }}
            >
              New coupon
            </Button>
          ) : undefined
        }
      />

      {editing !== null && (
        <form
          onSubmit={(event) => {
            event.preventDefault()
            save.mutate()
          }}
          className="card-surface animate-fade-rise mb-5 p-5"
        >
          <h2 className="font-display text-base font-bold text-ink-900">
            {editing === 'new' ? 'New coupon' : `Edit ${(editing as Coupon).code}`}
          </h2>

          {save.isError && (
            <div className="mt-3">
              <Alert tone="error">{(save.error as Error).message}</Alert>
            </div>
          )}

          <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <Field label="Code" htmlFor="code" required hint="Stored uppercase">
              <Input
                id="code"
                value={form.code}
                onChange={(event) => setForm((f) => ({ ...f, code: event.target.value }))}
                required
                className="uppercase"
              />
            </Field>

            <Field label="Type" htmlFor="type" required>
              <Select
                id="type"
                value={form.discountType}
                onChange={(event) => setForm((f) => ({ ...f, discountType: Number(event.target.value) }))}
              >
                <option value={0}>Percentage off</option>
                <option value={1}>Fixed amount off</option>
                <option value={2}>Free delivery</option>
              </Select>
            </Field>

            <Field
              label={form.discountType === 0 ? 'Percent' : 'Amount'}
              htmlFor="value"
              required
              hint={form.discountType === 0 ? '1–100' : 'In HKD'}
            >
              <Input
                id="value"
                type="number"
                min={0}
                step="0.01"
                value={form.value}
                onChange={(event) => setForm((f) => ({ ...f, value: Number(event.target.value) }))}
                required
              />
            </Field>

            <Field label="Minimum spend" htmlFor="minOrder" hint="Leave blank for none">
              <Input
                id="minOrder"
                type="number"
                min={0}
                value={form.minOrderAmount}
                onChange={(event) => setForm((f) => ({ ...f, minOrderAmount: event.target.value }))}
              />
            </Field>

            <Field label="Cap the discount at" htmlFor="maxDiscount" hint="Useful with a percentage">
              <Input
                id="maxDiscount"
                type="number"
                min={0}
                value={form.maxDiscountAmount}
                onChange={(event) => setForm((f) => ({ ...f, maxDiscountAmount: event.target.value }))}
              />
            </Field>

            <Field label="Expires" htmlFor="endsAt" hint="Leave blank for no expiry">
              <Input
                id="endsAt"
                type="date"
                value={form.endsAt}
                onChange={(event) => setForm((f) => ({ ...f, endsAt: event.target.value }))}
              />
            </Field>

            <Field label="Total uses" htmlFor="usageLimit" hint="Blank = unlimited">
              <Input
                id="usageLimit"
                type="number"
                min={1}
                value={form.usageLimit}
                onChange={(event) => setForm((f) => ({ ...f, usageLimit: event.target.value }))}
              />
            </Field>

            <Field label="Uses per customer" htmlFor="perCustomer" hint="Blank = unlimited">
              <Input
                id="perCustomer"
                type="number"
                min={1}
                value={form.usageLimitPerCustomer}
                onChange={(event) => setForm((f) => ({ ...f, usageLimitPerCustomer: event.target.value }))}
              />
            </Field>

            <Field label="Description" htmlFor="description">
              <Input
                id="description"
                value={form.description}
                onChange={(event) => setForm((f) => ({ ...f, description: event.target.value }))}
                placeholder="Shown to the customer"
              />
            </Field>
          </div>

          <div className="flex flex-wrap gap-4">
            <label className="flex items-center gap-2 text-sm text-ink-600">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(event) => setForm((f) => ({ ...f, isActive: event.target.checked }))}
                className="h-4 w-4 rounded border-ink-300 text-saffron-500"
              />
              Active
            </label>

            <label className="flex items-center gap-2 text-sm text-ink-600">
              <input
                type="checkbox"
                checked={form.firstOrderOnly}
                onChange={(event) => setForm((f) => ({ ...f, firstOrderOnly: event.target.checked }))}
                className="h-4 w-4 rounded border-ink-300 text-saffron-500"
              />
              First order only
            </label>
          </div>

          {/* A per-customer or first-order rule cannot be enforced against an anonymous shopper,
              so the server refuses it at checkout. Saying so here avoids a confusing rejection. */}
          {(form.firstOrderOnly || form.usageLimitPerCustomer) && (
            <p className="mt-3 text-xs text-ink-400">
              This code will require the shopper to be signed in, because the limit cannot be
              enforced against a guest.
            </p>
          )}

          <div className="mt-5 flex gap-2">
            <Button type="submit" loading={save.isPending}>
              Save coupon
            </Button>
            <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
          </div>
        </form>
      )}

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
            placeholder="Search codes…"
            className="h-9 w-64"
            aria-label="Search coupons"
          />
          <Button type="submit" variant="outline" size="sm">
            Search
          </Button>
        </form>
      </FilterBar>

      {isLoading ? (
        <TableSkeleton columns={6} />
      ) : (
        <>
          <TableShell>
            <thead>
              <tr>
                <Th>Code</Th>
                <Th>Discount</Th>
                <Th>Conditions</Th>
                <Th align="right">Used</Th>
                <Th>Status</Th>
                <Th align="right"> </Th>
              </tr>
            </thead>
            <tbody>
              {data?.items.length === 0 && <TableEmpty colSpan={6} message="No coupons yet." />}

              {data?.items.map((coupon) => (
                <tr key={coupon.id} className="transition-colors hover:bg-ink-50/60">
                  <Td>
                    <span className="font-mono text-sm font-bold text-ink-900">{coupon.code}</span>
                    {coupon.description && (
                      <span className="block truncate text-[11px] text-ink-400">{coupon.description}</span>
                    )}
                  </Td>

                  <Td className="text-ink-700">
                    {coupon.discountType === 0
                      ? `${coupon.value}% off`
                      : coupon.discountType === 1
                        ? `${formatPrice(coupon.value)} off`
                        : 'Free delivery'}
                    {coupon.maxDiscountAmount && (
                      <span className="block text-[11px] text-ink-400">
                        capped at {formatPrice(coupon.maxDiscountAmount)}
                      </span>
                    )}
                  </Td>

                  <Td className="text-[11px] text-ink-500">
                    {coupon.minOrderAmount ? `Min ${formatPrice(coupon.minOrderAmount)}` : 'No minimum'}
                    {coupon.firstOrderOnly && <span className="block">First order only</span>}
                    {coupon.endsAt && <span className="block">Until {formatDate(coupon.endsAt)}</span>}
                  </Td>

                  <Td align="right" className="text-ink-700">
                    {coupon.usedCount}
                    {coupon.usageLimit && <span className="text-ink-400"> / {coupon.usageLimit}</span>}
                  </Td>

                  <Td>
                    <Badge tone={coupon.isCurrentlyValid ? 'success' : 'neutral'}>
                      {coupon.isCurrentlyValid ? 'Live' : coupon.isActive ? 'Not live' : 'Inactive'}
                    </Badge>
                  </Td>

                  <Td align="right">
                    <div className="flex justify-end gap-1">
                      {can('coupons.update') && (
                        <Button variant="ghost" size="sm" onClick={() => startEdit(coupon)}>
                          Edit
                        </Button>
                      )}
                      {can('coupons.delete') && (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="text-chilli-600"
                          onClick={() => {
                            if (window.confirm(`Delete ${coupon.code}?`)) {
                              remove.mutate(coupon.id)
                            }
                          }}
                        >
                          Delete
                        </Button>
                      )}
                    </div>
                  </Td>
                </tr>
              ))}
            </tbody>
          </TableShell>

          {remove.isError && (
            <div className="mt-3">
              <Alert tone="error">{(remove.error as Error).message}</Alert>
            </div>
          )}

          {data && (
            <Pager page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} />
          )}
        </>
      )}
    </div>
  )
}
