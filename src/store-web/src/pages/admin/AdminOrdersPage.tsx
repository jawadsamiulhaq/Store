import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { keepPreviousData } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Alert, Button, Input, Select, Spinner, Textarea } from '../../ui/primitives'
import { FilterBar, PageHeader, Pager, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { OrderStatusPill, PaymentStatusPill } from '../../features/orders/OrderStatusPill'
import { OrderTimeline } from '../../features/orders/OrderTimeline'
import { useAuth } from '../../app/providers/AuthProvider'
import { formatDateTime, formatPrice } from '../../lib/format'
import { ORDER_STATUS_LABEL, OrderStatus, type OrderDetail, type Paged } from '../../lib/types'

interface AdminOrderRow {
  id: string
  orderNumber: string
  customerName: string
  email: string
  status: number
  paymentStatus: number
  fulfillmentStatus: number
  grandTotal: number
  currencyCode: string
  itemCount: number
  placedAt: string
}

/**
 * Valid next statuses, mirroring the server's transition table.
 *
 * The server is authoritative and rejects anything else with a 409 — this exists so staff are
 * offered only the moves that will actually work, rather than discovering the rule by hitting it.
 */
const NEXT_STATUS: Record<number, number[]> = {
  [OrderStatus.Pending]: [OrderStatus.Confirmed, OrderStatus.Cancelled],
  [OrderStatus.Confirmed]: [OrderStatus.Processing, OrderStatus.Cancelled],
  [OrderStatus.Processing]: [OrderStatus.Shipped, OrderStatus.Cancelled],
  [OrderStatus.Shipped]: [OrderStatus.Delivered, OrderStatus.Refunded],
  [OrderStatus.Delivered]: [OrderStatus.Refunded],
  [OrderStatus.Cancelled]: [],
  [OrderStatus.Refunded]: [],
}

export default function AdminOrdersPage() {
  const queryClient = useQueryClient()
  const { can } = useAuth()

  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<string>('')
  const [page, setPage] = useState(1)
  const [openId, setOpenId] = useState<string | null>(null)

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'orders', { search, status, page }],
    queryFn: () =>
      api.get<Paged<AdminOrderRow>>(
        `/admin/orders${qs({ search, status: status || undefined, page, pageSize: 20 })}`,
      ),
    placeholderData: keepPreviousData,
  })

  return (
    <div>
      <PageHeader title="Orders" description="Every order, newest first." />

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
            placeholder="Order number, email, phone, name…"
            className="h-9 w-72"
            aria-label="Search orders"
          />
          <Button type="submit" variant="outline" size="sm">
            Search
          </Button>
        </form>

        <Select
          value={status}
          onChange={(event) => {
            setStatus(event.target.value)
            setPage(1)
          }}
          className="h-9 w-44"
          aria-label="Filter by status"
        >
          <option value="">All statuses</option>
          {Object.entries(ORDER_STATUS_LABEL).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </Select>
      </FilterBar>

      {isLoading ? (
        <TableSkeleton columns={6} />
      ) : (
        <>
          <TableShell>
            <thead>
              <tr>
                <Th>Order</Th>
                <Th>Customer</Th>
                <Th>Placed</Th>
                <Th>Status</Th>
                <Th align="right">Total</Th>
                <Th align="right">Actions</Th>
              </tr>
            </thead>
            <tbody>
              {data?.items.length === 0 && <TableEmpty colSpan={6} message="No orders match those filters." />}

              {data?.items.map((order) => (
                <tr key={order.id} className="transition-colors hover:bg-ink-50/60">
                  <Td>
                    <span className="font-mono text-xs font-semibold text-ink-900">{order.orderNumber}</span>
                    <span className="block text-[11px] text-ink-400">
                      {order.itemCount} item{order.itemCount === 1 ? '' : 's'}
                    </span>
                  </Td>
                  <Td>
                    <span className="block truncate text-ink-800">{order.customerName}</span>
                    <span className="block truncate text-[11px] text-ink-400">{order.email}</span>
                  </Td>
                  <Td className="whitespace-nowrap text-xs text-ink-500">{formatDateTime(order.placedAt)}</Td>
                  <Td>
                    <div className="flex flex-col items-start gap-1">
                      <OrderStatusPill status={order.status} />
                      <PaymentStatusPill status={order.paymentStatus} />
                    </div>
                  </Td>
                  <Td align="right" className="font-semibold text-ink-900">
                    {formatPrice(order.grandTotal)}
                  </Td>
                  <Td align="right">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setOpenId(openId === order.id ? null : order.id)}
                    >
                      {openId === order.id ? 'Close' : 'Manage'}
                    </Button>
                  </Td>
                </tr>
              ))}
            </tbody>
          </TableShell>

          {data && (
            <Pager
              page={data.page}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onChange={setPage}
            />
          )}
        </>
      )}

      {openId && (
        <OrderDrawer
          orderId={openId}
          canUpdate={can('orders.update-status')}
          canShip={can('orders.manage-shipments')}
          onClose={() => setOpenId(null)}
          onChanged={() => {
            void queryClient.invalidateQueries({ queryKey: ['admin', 'orders'] })
            void queryClient.invalidateQueries({ queryKey: ['admin', 'dashboard'] })
          }}
        />
      )}
    </div>
  )
}

function OrderDrawer({
  orderId,
  canUpdate,
  canShip,
  onClose,
  onChanged,
}: {
  orderId: string
  canUpdate: boolean
  canShip: boolean
  onClose: () => void
  onChanged: () => void
}) {
  const queryClient = useQueryClient()
  const [note, setNote] = useState('')
  const [carrier, setCarrier] = useState('Gogo Van')
  const [tracking, setTracking] = useState('')

  const { data: order, isLoading } = useQuery({
    queryKey: ['admin', 'order', orderId],
    queryFn: () => api.get<OrderDetail>(`/admin/orders/${orderId}`),
  })

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['admin', 'order', orderId] })
    onChanged()
  }

  const updateStatus = useMutation({
    mutationFn: (next: number) =>
      api.put<OrderDetail>(`/admin/orders/${orderId}/status`, {
        status: next,
        note: note.trim() || null,
        notifyCustomer: true,
      }),
    onSuccess: async () => {
      setNote('')
      await refresh()
    },
  })

  const addShipment = useMutation({
    mutationFn: () =>
      api.post<OrderDetail>(`/admin/orders/${orderId}/shipments`, {
        carrier: carrier.trim(),
        trackingNumber: tracking.trim() || null,
        trackingUrl: null,
        estimatedDeliveryAt: null,
        note: null,
      }),
    onSuccess: async () => {
      setTracking('')
      await refresh()
    },
  })

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <button
        type="button"
        aria-label="Close"
        onClick={onClose}
        className="animate-fade-in absolute inset-0 bg-ink-900/40"
      />

      <div className="animate-fade-rise relative flex w-full max-w-lg flex-col overflow-y-auto bg-paper shadow-overlay">
        <header className="sticky top-0 flex items-center justify-between border-b border-ink-100 bg-paper-raised px-5 py-4">
          <div>
            <p className="font-mono text-sm font-bold text-ink-900">{order?.orderNumber ?? '…'}</p>
            {order && <p className="text-xs text-ink-400">{formatDateTime(order.placedAt)}</p>}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="grid h-9 w-9 place-items-center rounded-lg text-ink-500 hover:bg-ink-100"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <path d="M6 6l12 12M18 6 6 18" strokeLinecap="round" />
            </svg>
          </button>
        </header>

        {isLoading || !order ? (
          <div className="p-5">
            <Spinner className="h-6 w-6 text-ink-400" />
          </div>
        ) : (
          <div className="space-y-5 p-5">
            <div className="flex flex-wrap items-center gap-2">
              <OrderStatusPill status={order.status} />
              <PaymentStatusPill status={order.paymentStatus} />
            </div>

            {canUpdate && NEXT_STATUS[order.status]?.length > 0 && (
              <section className="card-surface p-4">
                <h3 className="text-sm font-semibold text-ink-800">Move this order on</h3>

                {updateStatus.isError && (
                  <div className="mt-2">
                    <Alert tone="error">{(updateStatus.error as Error).message}</Alert>
                  </div>
                )}

                <Textarea
                  rows={2}
                  value={note}
                  onChange={(event) => setNote(event.target.value)}
                  placeholder="Note for the customer (optional)"
                  aria-label="Status note"
                  className="mt-3"
                />

                <div className="mt-3 flex flex-wrap gap-2">
                  {NEXT_STATUS[order.status].map((next) => (
                    <Button
                      key={next}
                      size="sm"
                      variant={next === OrderStatus.Cancelled || next === OrderStatus.Refunded ? 'danger' : 'primary'}
                      loading={updateStatus.isPending}
                      onClick={() => updateStatus.mutate(next)}
                    >
                      {ORDER_STATUS_LABEL[next]}
                    </Button>
                  ))}
                </div>

                {/* Cancelling and refunding both return stock and release the coupon — worth
                    stating plainly, because it is not reversible. */}
                <p className="mt-2 text-[11px] leading-relaxed text-ink-400">
                  Cancelling or refunding returns the stock to the shelf and releases any discount
                  code used. This cannot be undone.
                </p>
              </section>
            )}

            {canShip && order.status !== OrderStatus.Cancelled && order.status !== OrderStatus.Refunded && (
              <section className="card-surface p-4">
                <h3 className="text-sm font-semibold text-ink-800">Add tracking</h3>

                <div className="mt-3 grid gap-2 sm:grid-cols-2">
                  <Input
                    value={carrier}
                    onChange={(event) => setCarrier(event.target.value)}
                    placeholder="Carrier"
                    aria-label="Carrier"
                    className="h-9"
                  />
                  <Input
                    value={tracking}
                    onChange={(event) => setTracking(event.target.value)}
                    placeholder="Tracking number"
                    aria-label="Tracking number"
                    className="h-9"
                  />
                </div>

                <Button
                  size="sm"
                  variant="outline"
                  className="mt-3"
                  loading={addShipment.isPending}
                  disabled={!carrier.trim()}
                  onClick={() => addShipment.mutate()}
                >
                  Add shipment
                </Button>
                <p className="mt-2 text-[11px] text-ink-400">
                  Adding tracking also marks the order as shipped.
                </p>
              </section>
            )}

            <section className="card-surface p-4">
              <h3 className="text-sm font-semibold text-ink-800">Progress</h3>
              <div className="mt-3">
                <OrderTimeline order={order} />
              </div>
            </section>

            <section className="card-surface p-4">
              <h3 className="text-sm font-semibold text-ink-800">Items</h3>
              <ul className="mt-3 space-y-2">
                {order.items.map((item) => (
                  <li key={item.id} className="flex justify-between gap-3 text-xs">
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-ink-700">{item.productName}</span>
                      <span className="block text-ink-400">
                        {item.sku} · {item.quantity} × {formatPrice(item.unitPrice)}
                      </span>
                    </span>
                    <span className="shrink-0 font-semibold text-ink-800">{formatPrice(item.lineTotal)}</span>
                  </li>
                ))}
              </ul>

              <dl className="mt-3 space-y-1 border-t border-ink-100 pt-3 text-xs">
                <div className="flex justify-between">
                  <dt className="text-ink-500">Subtotal</dt>
                  <dd className="text-ink-700">{formatPrice(order.subtotal)}</dd>
                </div>
                {order.discountTotal > 0 && (
                  <div className="flex justify-between">
                    <dt className="text-ink-500">Discount {order.couponCode && `(${order.couponCode})`}</dt>
                    <dd className="text-cardamom-600">−{formatPrice(order.discountTotal)}</dd>
                  </div>
                )}
                <div className="flex justify-between">
                  <dt className="text-ink-500">Delivery</dt>
                  <dd className="text-ink-700">{formatPrice(order.shippingTotal)}</dd>
                </div>
                <div className="flex justify-between border-t border-ink-100 pt-1.5">
                  <dt className="font-semibold text-ink-900">Total</dt>
                  <dd className="font-bold text-ink-900">{formatPrice(order.grandTotal)}</dd>
                </div>
              </dl>
            </section>

            <section className="card-surface p-4 text-xs">
              <h3 className="text-sm font-semibold text-ink-800">Delivering to</h3>
              <address className="mt-2 not-italic leading-relaxed text-ink-600">
                {order.shippingAddress.fullName}
                <br />
                {order.shippingAddress.line1}
                {order.shippingAddress.line2 && (
                  <>
                    <br />
                    {order.shippingAddress.line2}
                  </>
                )}
                <br />
                {[order.shippingAddress.district, order.shippingAddress.region].filter(Boolean).join(', ')}
                <br />
                {order.shippingAddress.phone} · {order.email}
              </address>

              {order.customerNote && (
                <p className="mt-3 rounded-lg bg-saffron-50 p-2.5 text-saffron-800">
                  <strong>Customer note:</strong> {order.customerNote}
                </p>
              )}
            </section>
          </div>
        )}
      </div>
    </div>
  )
}
