import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Alert, Button, Field, Input } from '../../ui/primitives'
import { OrderTimeline } from '../../features/orders/OrderTimeline'
import { formatPrice } from '../../lib/format'
import type { OrderDetail } from '../../lib/types'

/**
 * Public order tracking.
 *
 * Requires the order number *and* the email it was placed with. The server returns the same
 * "not found" for a wrong email as for a missing order, so this form cannot be used to discover
 * which order numbers exist.
 */
export default function TrackOrderPage() {
  const [searchParams] = useSearchParams()
  const [orderNumber, setOrderNumber] = useState(searchParams.get('orderNumber') ?? '')
  const [email, setEmail] = useState('')

  const track = useMutation({
    mutationFn: () =>
      api.get<OrderDetail>(`/orders/track${qs({ orderNumber: orderNumber.trim(), email: email.trim() })}`),
  })

  return (
    <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6">
      <h1 className="text-2xl font-bold tracking-tight text-ink-900 sm:text-3xl">Track your order</h1>
      <p className="mt-2 text-ink-500">
        Enter your order number and the email address you used, and we will show you where it is.
      </p>

      <form
        onSubmit={(event) => {
          event.preventDefault()
          track.mutate()
        }}
        className="card-surface mt-6 p-5"
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Order number" htmlFor="orderNumber" required hint="e.g. WPS-20260924-0001">
            <Input
              id="orderNumber"
              value={orderNumber}
              onChange={(event) => setOrderNumber(event.target.value)}
              placeholder="WPS-…"
              autoComplete="off"
              className="font-mono uppercase"
            />
          </Field>

          <Field label="Email" htmlFor="trackEmail" required>
            <Input
              id="trackEmail"
              type="email"
              inputMode="email"
              autoComplete="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          </Field>
        </div>

        <Button
          type="submit"
          loading={track.isPending}
          disabled={!orderNumber.trim() || !email.trim()}
          className="mt-1"
        >
          Find my order
        </Button>

        {track.isError && (
          <div className="mt-4">
            <Alert tone="error">{(track.error as Error).message}</Alert>
          </div>
        )}
      </form>

      {track.data && (
        <div className="animate-fade-rise mt-6 space-y-4">
          <div className="card-surface p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <p className="text-xs uppercase tracking-wide text-ink-400">Order</p>
                <p className="font-mono text-lg font-bold text-ink-900">{track.data.orderNumber}</p>
              </div>
              <div className="text-right">
                <p className="text-xs uppercase tracking-wide text-ink-400">Total</p>
                <p className="text-lg font-bold text-ink-900">{formatPrice(track.data.grandTotal)}</p>
              </div>
            </div>
          </div>

          <div className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Progress</h2>
            <div className="mt-4">
              <OrderTimeline order={track.data} />
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
