import { formatDateTime } from '../../lib/format'
import { ORDER_STATUS_LABEL, OrderStatus, type OrderDetail } from '../../lib/types'

/**
 * Order progress.
 *
 * Renders the *expected* journey with each step marked done, current or pending — rather than only
 * the events that have happened. A customer wants to know what is still to come, not just what
 * already did, and a list that grows one row at a time gives no sense of how far along an order is.
 *
 * A cancelled or refunded order abandons the ladder entirely, because showing "Delivered" as a
 * pending future step on a cancelled order would be actively misleading.
 */
const JOURNEY = [
  { status: OrderStatus.Pending, label: 'Order placed', hint: 'We have your order' },
  { status: OrderStatus.Confirmed, label: 'Confirmed', hint: 'Payment and stock checked' },
  { status: OrderStatus.Processing, label: 'Being prepared', hint: 'Packing your items' },
  { status: OrderStatus.Shipped, label: 'On its way', hint: 'Out for delivery' },
  { status: OrderStatus.Delivered, label: 'Delivered', hint: 'Enjoy!' },
] as const

export function OrderTimeline({ order }: { order: OrderDetail }) {
  const isTerminated = order.status === OrderStatus.Cancelled || order.status === OrderStatus.Refunded

  if (isTerminated) {
    return (
      <div className="rounded-xl border border-chilli-200 bg-chilli-50 p-4">
        <p className="font-semibold text-chilli-700">
          {ORDER_STATUS_LABEL[order.status]}
          {order.cancelledAt && (
            <span className="ml-2 font-normal text-chilli-600">{formatDateTime(order.cancelledAt)}</span>
          )}
        </p>
        {order.cancelReason && <p className="mt-1 text-sm text-chilli-600">{order.cancelReason}</p>}

        {/* The factual history is still shown — it is the forward-looking ladder that is dropped. */}
        {order.timeline.length > 0 && (
          <ul className="mt-4 space-y-2 border-t border-chilli-200 pt-3">
            {order.timeline.map((entry, index) => (
              <li key={index} className="flex justify-between gap-3 text-xs text-chilli-700">
                <span>{ORDER_STATUS_LABEL[entry.status] ?? 'Updated'}</span>
                <time dateTime={entry.createdAt}>{formatDateTime(entry.createdAt)}</time>
              </li>
            ))}
          </ul>
        )}
      </div>
    )
  }

  // Timestamps come from the order's own fields where available, so each step shows when it
  // actually happened rather than just that it did.
  const timestamps: Partial<Record<number, string | undefined>> = {
    [OrderStatus.Pending]: order.placedAt,
    [OrderStatus.Confirmed]: order.confirmedAt,
    [OrderStatus.Shipped]: order.shippedAt,
    [OrderStatus.Delivered]: order.deliveredAt,
  }

  const currentIndex = JOURNEY.findIndex((step) => step.status === order.status)

  return (
    <ol className="relative">
      {JOURNEY.map((step, index) => {
        const isDone = index < currentIndex
        const isCurrent = index === currentIndex
        const isLast = index === JOURNEY.length - 1
        const at = timestamps[step.status]

        return (
          <li key={step.status} className="relative flex gap-4 pb-6 last:pb-0">
            {/* Connector. Drawn behind the markers and stopped before the last one. */}
            {!isLast && (
              <span
                className={`absolute left-[11px] top-6 h-full w-0.5 ${
                  isDone ? 'bg-cardamom-400' : 'bg-ink-200'
                }`}
                aria-hidden="true"
              />
            )}

            <span
              className={`relative z-10 grid h-6 w-6 shrink-0 place-items-center rounded-full border-2 ${
                isDone
                  ? 'border-cardamom-500 bg-cardamom-500 text-white'
                  : isCurrent
                    ? 'border-saffron-500 bg-saffron-50 text-saffron-600'
                    : 'border-ink-200 bg-paper-raised text-ink-300'
              }`}
              aria-hidden="true"
            >
              {isDone ? (
                <svg className="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3">
                  <path d="m5 13 4 4L19 7" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              ) : (
                <span
                  className={`h-1.5 w-1.5 rounded-full ${isCurrent ? 'bg-saffron-500' : 'bg-ink-200'}`}
                />
              )}
            </span>

            <div className="min-w-0 flex-1 pt-0.5">
              <p
                className={`text-sm font-medium ${
                  isCurrent ? 'text-saffron-700' : isDone ? 'text-ink-800' : 'text-ink-400'
                }`}
              >
                {step.label}
                {isCurrent && (
                  <span className="ml-2 rounded-full bg-saffron-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-saffron-700">
                    Now
                  </span>
                )}
              </p>

              <p className="text-xs text-ink-400">
                {at ? formatDateTime(at) : step.hint}
              </p>
            </div>
          </li>
        )
      })}

      {order.shipments.length > 0 && (
        <li className="mt-2 rounded-xl border border-ink-200 bg-paper-sunken p-3">
          {order.shipments.map((shipment) => (
            <div key={shipment.id} className="text-xs">
              <p className="font-medium text-ink-700">Shipped with {shipment.carrier}</p>
              {shipment.trackingNumber && (
                <p className="mt-0.5 text-ink-500">
                  Tracking:{' '}
                  {shipment.trackingUrl ? (
                    <a
                      href={shipment.trackingUrl}
                      target="_blank"
                      // noreferrer alongside noopener: the courier should not receive our URL
                      // as a referrer, and the new tab must not get a handle on this window.
                      rel="noopener noreferrer"
                      className="font-mono underline hover:text-saffron-600"
                    >
                      {shipment.trackingNumber}
                    </a>
                  ) : (
                    <span className="font-mono">{shipment.trackingNumber}</span>
                  )}
                </p>
              )}
              {shipment.estimatedDeliveryAt && (
                <p className="mt-0.5 text-ink-500">
                  Estimated arrival {formatDateTime(shipment.estimatedDeliveryAt)}
                </p>
              )}
            </div>
          ))}
        </li>
      )}
    </ol>
  )
}
