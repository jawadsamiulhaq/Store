import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { Image } from '../../ui/Image'
import { Alert, Button, ButtonLink, EmptyState, Textarea } from '../../ui/primitives'
import { OrderTimeline } from '../../features/orders/OrderTimeline'
import { OrderStatusPill, PaymentStatusPill } from '../../features/orders/OrderStatusPill'
import { formatDateTime, formatPrice } from '../../lib/format'
import type { OrderDetail } from '../../lib/types'

export default function OrderDetailPage() {
  const { id } = useParams()
  const queryClient = useQueryClient()
  const [cancelling, setCancelling] = useState(false)
  const [reason, setReason] = useState('')

  const { data: order, isLoading } = useQuery({
    queryKey: ['order', id],
    queryFn: () => api.get<OrderDetail>(`/orders/${id}`),
    enabled: Boolean(id),
  })

  const cancel = useMutation({
    mutationFn: () => api.post<void>(`/orders/${id}/cancel`, { reason: reason.trim() || 'Changed my mind' }),
    onSuccess: async () => {
      setCancelling(false)
      setReason('')
      await queryClient.invalidateQueries({ queryKey: ['order', id] })
      await queryClient.invalidateQueries({ queryKey: ['orders'] })
    },
  })

  if (isLoading) {
    return (
      <div className="space-y-4" aria-busy="true">
        <div className="skeleton h-24 w-full" />
        <div className="skeleton h-64 w-full" />
      </div>
    )
  }

  if (!order) {
    return (
      <EmptyState
        title="Order not found"
        description="This order does not exist, or it belongs to another account."
        action={<ButtonLink to="/account/orders">Back to my orders</ButtonLink>}
      />
    )
  }

  return (
    <div className="space-y-4">
      <Link to="/account/orders" className="inline-block text-sm text-ink-500 hover:text-saffron-600">
        ← All orders
      </Link>

      <div className="card-surface p-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <p className="text-xs uppercase tracking-wide text-ink-400">Order</p>
            <p className="font-mono text-xl font-bold text-ink-900">{order.orderNumber}</p>
            <p className="mt-1 text-xs text-ink-500">Placed {formatDateTime(order.placedAt)}</p>
          </div>

          <div className="flex flex-col items-end gap-2">
            <OrderStatusPill status={order.status} />
            <PaymentStatusPill status={order.paymentStatus} />
          </div>
        </div>
      </div>

      <div className="card-surface p-5">
        <h2 className="font-display text-base font-bold text-ink-900">Progress</h2>
        <div className="mt-4">
          <OrderTimeline order={order} />
        </div>
      </div>

      <div className="card-surface p-5">
        <h2 className="font-display text-base font-bold text-ink-900">Items</h2>

        <ul className="mt-4 space-y-3">
          {order.items.map((item) => (
            <li key={item.id} className="flex items-center gap-3">
              {/* Links back to the product only when it still exists in the catalogue. */}
              {item.productSlug ? (
                <Link to={`/product/${item.productSlug}`} className="shrink-0">
                  <Image src={item.imageUrl} alt="" width={56} height={56} className="h-14 w-14 rounded-lg" />
                </Link>
              ) : (
                <Image src={item.imageUrl} alt="" width={56} height={56} className="h-14 w-14 shrink-0 rounded-lg" />
              )}

              <div className="min-w-0 flex-1">
                <p className="truncate text-sm text-ink-800">{item.productName}</p>
                <p className="text-xs text-ink-400">
                  {item.variantName ? `${item.variantName} · ` : ''}
                  {formatPrice(item.unitPrice)} × {item.quantity}
                </p>
              </div>

              <span className="shrink-0 text-sm font-semibold text-ink-900">
                {formatPrice(item.lineTotal)}
              </span>
            </li>
          ))}
        </ul>

        <dl className="mt-5 space-y-2 border-t border-ink-100 pt-4 text-sm">
          <div className="flex justify-between">
            <dt className="text-ink-600">Subtotal</dt>
            <dd className="text-ink-800">{formatPrice(order.subtotal)}</dd>
          </div>
          {order.discountTotal > 0 && (
            <div className="flex justify-between">
              <dt className="text-ink-600">Discount {order.couponCode && `(${order.couponCode})`}</dt>
              <dd className="text-cardamom-600">−{formatPrice(order.discountTotal)}</dd>
            </div>
          )}
          <div className="flex justify-between">
            <dt className="text-ink-600">Delivery</dt>
            <dd className="text-ink-800">
              {order.shippingTotal === 0 ? 'Free' : formatPrice(order.shippingTotal)}
            </dd>
          </div>
          <div className="flex items-baseline justify-between border-t border-ink-100 pt-2">
            <dt className="font-semibold text-ink-900">Total</dt>
            <dd className="text-lg font-bold text-ink-900">{formatPrice(order.grandTotal)}</dd>
          </div>
        </dl>
      </div>

      <div className="card-surface grid gap-5 p-5 sm:grid-cols-2">
        <div>
          <h3 className="text-sm font-semibold text-ink-800">Delivery address</h3>
          <address className="mt-1.5 text-sm not-italic leading-relaxed text-ink-600">
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
            {order.shippingAddress.phone}
          </address>
        </div>

        <div>
          <h3 className="text-sm font-semibold text-ink-800">Delivery method</h3>
          <p className="mt-1.5 text-sm leading-relaxed text-ink-600">
            {order.shippingMethodName ?? '—'}
            {order.customerNote && (
              <>
                <br />
                <span className="text-xs text-ink-400">Note: {order.customerNote}</span>
              </>
            )}
          </p>
        </div>
      </div>

      {/* Cancellation is offered only while the server would actually allow it, so the button
          cannot promise something the API will refuse. */}
      {order.isCancellable && (
        <div className="card-surface p-5">
          {cancelling ? (
            <div>
              <h3 className="text-sm font-semibold text-ink-800">Cancel this order</h3>
              <p className="mt-1 text-xs text-ink-500">
                Stock will go back on the shelf and any discount code will be released.
              </p>

              {cancel.isError && (
                <div className="mt-3">
                  <Alert tone="error">{(cancel.error as Error).message}</Alert>
                </div>
              )}

              <div className="mt-3">
                <Textarea
                  rows={2}
                  placeholder="Let us know why (optional)"
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                  aria-label="Reason for cancelling"
                />
              </div>

              <div className="mt-3 flex gap-2">
                <Button variant="danger" loading={cancel.isPending} onClick={() => cancel.mutate()}>
                  Yes, cancel the order
                </Button>
                <Button variant="ghost" onClick={() => setCancelling(false)}>
                  Keep it
                </Button>
              </div>
            </div>
          ) : (
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="text-sm text-ink-500">Changed your mind? You can still cancel this order.</p>
              <Button variant="outline" size="sm" onClick={() => setCancelling(true)}>
                Cancel order
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
