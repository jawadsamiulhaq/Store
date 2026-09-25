import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Image } from '../../ui/Image'
import { Alert, Button, ButtonLink, EmptyState, Input, Spinner } from '../../ui/primitives'
import { useCart, useCartMutations } from '../../features/cart/useCart'
import { useStore } from '../../app/providers/StoreProvider'
import { formatPrice, formatUnit } from '../../lib/format'

export default function CartPage() {
  const { data: cart, isLoading } = useCart()
  const { updateQuantity, removeItem, applyCoupon, removeCoupon } = useCartMutations()
  const { settingNumber } = useStore()
  const [couponCode, setCouponCode] = useState('')

  const freeShippingThreshold = settingNumber('checkout.free-shipping-threshold', 300)
  const minimumOrder = settingNumber('checkout.min-order-amount', 0)

  if (isLoading) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-10 sm:px-6" aria-busy="true">
        <div className="skeleton h-8 w-48" />
        <div className="mt-6 space-y-3">
          {Array.from({ length: 3 }, (_, index) => (
            <div key={index} className="skeleton h-28 w-full" />
          ))}
        </div>
      </div>
    )
  }

  if (!cart || cart.items.length === 0) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6">
        <EmptyState
          title="Your cart is empty"
          description="Browse the aisles and add a few pantry staples."
          action={<ButtonLink to="/shop">Start shopping</ButtonLink>}
        />
      </div>
    )
  }

  const { totals } = cart
  const awayFromFreeShipping = freeShippingThreshold - totals.total
  const belowMinimum = minimumOrder > 0 && totals.subtotal < minimumOrder

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <h1 className="text-2xl font-bold tracking-tight text-ink-900 sm:text-3xl">
        Your cart
        <span className="ml-2 text-base font-normal text-ink-400">
          ({totals.itemCount} item{totals.itemCount === 1 ? '' : 's'})
        </span>
      </h1>

      {/* Server-side warnings — a price moved, or stock ran low while the item sat in the cart. */}
      {cart.warnings.length > 0 && (
        <div className="mt-4 space-y-2">
          {cart.warnings.map((warning) => (
            <Alert key={warning} tone="warning">
              {warning}
            </Alert>
          ))}
        </div>
      )}

      <div className="mt-6 grid gap-8 lg:grid-cols-[1fr_22rem]">
        <ul className="space-y-3">
          {cart.items.map((item) => (
            <li key={item.id} className="card-surface flex gap-4 p-3 sm:p-4">
              <Link to={`/product/${item.productSlug}`} className="shrink-0">
                <Image
                  src={item.imageUrl}
                  alt={item.productName}
                  width={96}
                  height={96}
                  className="h-24 w-24 rounded-lg"
                />
              </Link>

              <div className="flex min-w-0 flex-1 flex-col">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <Link
                      to={`/product/${item.productSlug}`}
                      className="line-clamp-2 text-sm font-medium text-ink-800 hover:text-saffron-600"
                    >
                      {item.productName}
                    </Link>

                    {(item.variantName || item.unitValue) && (
                      <p className="mt-0.5 text-xs text-ink-400">
                        {item.variantName ?? formatUnit(item.unit, item.unitValue)}
                      </p>
                    )}

                    {!item.isAvailable && (
                      <p className="mt-1 text-xs font-medium text-chilli-600">
                        {item.availableQuantity === 0
                          ? 'Now out of stock'
                          : `Only ${item.availableQuantity} available`}
                      </p>
                    )}

                    {item.priceChanged && (
                      <p className="mt-1 text-xs font-medium text-saffron-700">
                        Price changed from {formatPrice(item.priceWhenAdded)}
                      </p>
                    )}
                  </div>

                  <button
                    type="button"
                    onClick={() => removeItem.mutate(item.id)}
                    aria-label={`Remove ${item.productName} from cart`}
                    className="shrink-0 rounded-lg p-1.5 text-ink-300 transition-colors hover:bg-chilli-50 hover:text-chilli-500"
                  >
                    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                      <path d="M6 6l12 12M18 6 6 18" strokeLinecap="round" />
                    </svg>
                  </button>
                </div>

                <div className="mt-auto flex items-end justify-between gap-3 pt-2">
                  <div className="flex items-center rounded-lg border border-ink-200">
                    <button
                      type="button"
                      onClick={() => updateQuantity.mutate({ itemId: item.id, quantity: item.quantity - 1 })}
                      aria-label="Decrease quantity"
                      className="grid h-9 w-9 place-items-center rounded-l-lg text-ink-600 transition-colors hover:bg-ink-50"
                    >
                      −
                    </button>
                    <span className="w-9 text-center text-sm font-semibold text-ink-800">{item.quantity}</span>
                    <button
                      type="button"
                      onClick={() => updateQuantity.mutate({ itemId: item.id, quantity: item.quantity + 1 })}
                      disabled={item.quantity >= Math.min(item.availableQuantity, 99)}
                      aria-label="Increase quantity"
                      className="grid h-9 w-9 place-items-center rounded-r-lg text-ink-600 transition-colors hover:bg-ink-50 disabled:opacity-40"
                    >
                      +
                    </button>
                  </div>

                  <div className="text-right">
                    <p className="text-sm font-semibold text-ink-900">{formatPrice(item.lineTotal)}</p>
                    {item.quantity > 1 && (
                      <p className="text-xs text-ink-400">{formatPrice(item.unitPrice)} each</p>
                    )}
                  </div>
                </div>
              </div>
            </li>
          ))}
        </ul>

        {/* ---- Summary ---- */}
        <aside className="lg:sticky lg:top-36 lg:self-start">
          <div className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Order summary</h2>

            <dl className="mt-4 space-y-2.5 text-sm">
              <Row label="Subtotal" value={formatPrice(totals.subtotal)} />

              {totals.discountTotal > 0 && (
                <Row
                  label={`Discount${cart.coupon ? ` (${cart.coupon.code})` : ''}`}
                  value={`−${formatPrice(totals.discountTotal)}`}
                  tone="positive"
                />
              )}

              <Row label="Delivery" value="Calculated at checkout" muted />

              <div className="border-t border-ink-100 pt-2.5">
                <div className="flex items-baseline justify-between">
                  <dt className="font-semibold text-ink-900">Total</dt>
                  <dd className="text-xl font-bold text-ink-900">{formatPrice(totals.total)}</dd>
                </div>
              </div>
            </dl>

            {/* Free-shipping nudge. Progress bar width comes straight from data, never animated. */}
            {awayFromFreeShipping > 0 && (
              <div className="mt-4 rounded-lg bg-saffron-50 p-3">
                <p className="text-xs text-saffron-800">
                  Add <strong>{formatPrice(awayFromFreeShipping)}</strong> more for free delivery in Kowloon.
                </p>
                <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-saffron-100">
                  <div
                    className="h-full rounded-full bg-saffron-500"
                    style={{ width: `${Math.min(100, (totals.total / freeShippingThreshold) * 100)}%` }}
                  />
                </div>
              </div>
            )}

            {/* ---- Coupon ---- */}
            <div className="mt-4 border-t border-ink-100 pt-4">
              {cart.coupon ? (
                <div className="flex items-center justify-between gap-2 rounded-lg bg-cardamom-50 px-3 py-2">
                  <div className="min-w-0">
                    <p className="text-sm font-semibold text-cardamom-700">{cart.coupon.code}</p>
                    {cart.coupon.description && (
                      <p className="truncate text-xs text-cardamom-600">{cart.coupon.description}</p>
                    )}
                  </div>
                  <button
                    type="button"
                    onClick={() => removeCoupon.mutate()}
                    className="shrink-0 text-xs font-medium text-cardamom-700 underline"
                  >
                    Remove
                  </button>
                </div>
              ) : (
                <form
                  onSubmit={(event) => {
                    event.preventDefault()
                    if (couponCode.trim()) {
                      applyCoupon.mutate(couponCode.trim(), { onSuccess: () => setCouponCode('') })
                    }
                  }}
                  className="flex gap-2"
                >
                  <label htmlFor="coupon" className="sr-only">
                    Discount code
                  </label>
                  <Input
                    id="coupon"
                    value={couponCode}
                    onChange={(event) => setCouponCode(event.target.value)}
                    placeholder="Discount code"
                    autoComplete="off"
                    className="h-10 uppercase"
                  />
                  <Button type="submit" variant="outline" size="md" disabled={applyCoupon.isPending}>
                    {applyCoupon.isPending ? <Spinner /> : 'Apply'}
                  </Button>
                </form>
              )}

              {applyCoupon.isError && (
                <p className="mt-2 text-xs text-chilli-600">{(applyCoupon.error as Error).message}</p>
              )}
            </div>

            {belowMinimum && (
              <div className="mt-4">
                <Alert tone="warning">
                  The minimum order is {formatPrice(minimumOrder)}. Add{' '}
                  {formatPrice(minimumOrder - totals.subtotal)} more to check out.
                </Alert>
              </div>
            )}

            <div className="mt-5">
              <ButtonLink to="/checkout" size="lg" fullWidth>
                Proceed to checkout
              </ButtonLink>
            </div>

            <Link
              to="/shop"
              className="mt-3 block text-center text-sm text-ink-500 transition-colors hover:text-saffron-600"
            >
              Continue shopping
            </Link>
          </div>
        </aside>
      </div>
    </div>
  )
}

function Row({
  label,
  value,
  tone,
  muted,
}: {
  label: string
  value: string
  tone?: 'positive'
  muted?: boolean
}) {
  return (
    <div className="flex items-baseline justify-between gap-2">
      <dt className={muted ? 'text-ink-400' : 'text-ink-600'}>{label}</dt>
      <dd
        className={
          tone === 'positive'
            ? 'font-medium text-cardamom-600'
            : muted
              ? 'text-xs text-ink-400'
              : 'font-medium text-ink-800'
        }
      >
        {value}
      </dd>
    </div>
  )
}
