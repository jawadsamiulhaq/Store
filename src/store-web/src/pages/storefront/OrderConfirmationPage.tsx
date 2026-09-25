import { Link, useLocation, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { Image } from '../../ui/Image'
import { ButtonLink, EmptyState } from '../../ui/primitives'
import { useAuth } from '../../app/providers/AuthProvider'
import { formatDate, formatPrice } from '../../lib/format'
import type { OrderDetail } from '../../lib/types'

export default function OrderConfirmationPage() {
  const { id } = useParams()
  const location = useLocation()
  const { isAuthenticated } = useAuth()

  // Checkout navigates here with the order in router state, so the confirmation paints instantly
  // rather than flashing a spinner for an order we already have in hand.
  const handedOver = (location.state as { order?: OrderDetail } | null)?.order

  const { data: fetched } = useQuery({
    queryKey: ['order', id],
    queryFn: () => api.get<OrderDetail>(`/orders/${id}`),
    // Only fetched when we were not handed the order, and only for signed-in customers — a guest
    // has no authenticated route to re-read it.
    enabled: !handedOver && Boolean(id) && isAuthenticated,
  })

  const order = handedOver ?? fetched

  if (!order) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6">
        <EmptyState
          title="Order placed"
          description="Your order has been received. Check your email for the confirmation."
          action={<ButtonLink to="/shop">Continue shopping</ButtonLink>}
        />
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6">
      <div className="text-center">
        <span className="animate-pop mx-auto grid h-16 w-16 place-items-center rounded-full bg-success-100 text-success-700">
          <svg className="h-8 w-8" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" aria-hidden="true">
            <path d="m5 13 4 4L19 7" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </span>

        <h1 className="mt-5 text-2xl font-bold tracking-tight text-ink-900 sm:text-3xl">
          Thank you — your order is in
        </h1>
        <p className="mt-2 text-ink-500">
          We have sent a confirmation to <strong className="text-ink-700">{order.email}</strong>.
        </p>

        {/* The order number is the one thing a customer needs to keep, so it is the largest,
            most copyable element on the page. */}
        <div className="mt-6 inline-block rounded-xl border border-ink-200 bg-paper-raised px-6 py-4">
          <p className="text-xs uppercase tracking-wide text-ink-400">Order number</p>
          <p className="mt-1 font-mono text-xl font-bold tracking-tight text-ink-900">{order.orderNumber}</p>
        </div>
      </div>

      <div className="card-surface mt-8 p-5">
        <h2 className="font-display text-base font-bold text-ink-900">What you ordered</h2>

        <ul className="mt-4 space-y-3">
          {order.items.map((item) => (
            <li key={item.id} className="flex items-center gap-3">
              <Image src={item.imageUrl} alt="" width={48} height={48} className="h-12 w-12 shrink-0 rounded-lg" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm text-ink-700">{item.productName}</p>
                <p className="text-xs text-ink-400">
                  {item.variantName ? `${item.variantName} · ` : ''}Qty {item.quantity}
                </p>
              </div>
              <span className="text-sm font-semibold text-ink-800">{formatPrice(item.lineTotal)}</span>
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
              <dt className="text-ink-600">Discount</dt>
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

      <div className="card-surface mt-4 grid gap-5 p-5 sm:grid-cols-2">
        <div>
          <h3 className="text-sm font-semibold text-ink-800">Delivering to</h3>
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
          <h3 className="text-sm font-semibold text-ink-800">Delivery &amp; payment</h3>
          <p className="mt-1.5 text-sm leading-relaxed text-ink-600">
            {order.shippingMethodName}
            <br />
            {order.paymentMethod === 0 ? 'Cash on delivery' : 'Payment details to follow by email'}
            <br />
            Placed {formatDate(order.placedAt)}
          </p>
        </div>
      </div>

      <div className="mt-8 flex flex-wrap justify-center gap-3">
        <ButtonLink to="/shop" variant="outline">
          Continue shopping
        </ButtonLink>

        {isAuthenticated ? (
          <ButtonLink to={`/account/orders/${order.id}`}>Track this order</ButtonLink>
        ) : (
          // A guest has no account to track from, so they are pointed at the public lookup with
          // the number they will need.
          <ButtonLink to={`/track?orderNumber=${order.orderNumber}`}>Track this order</ButtonLink>
        )}
      </div>

      {!isAuthenticated && (
        <p className="mt-4 text-center text-xs text-ink-400">
          Keep your order number safe — you will need it along with your email to track this order.{' '}
          <Link to="/register" className="underline hover:text-ink-600">
            Create an account
          </Link>{' '}
          to see all your orders in one place.
        </p>
      )}
    </div>
  )
}
