import { ORDER_STATUS_LABEL, OrderStatus } from '../../lib/types'

/**
 * Status pill, used in both the customer's order list and the admin queue so a status never
 * means two different-looking things in two places.
 *
 * Colour carries meaning, but never alone — the label is always present, because roughly one in
 * twelve men has a colour-vision deficiency and a colour-only status is invisible to them.
 */
const TONES: Record<number, string> = {
  [OrderStatus.Pending]: 'bg-ink-100 text-ink-700',
  [OrderStatus.Confirmed]: 'bg-saffron-100 text-saffron-800',
  [OrderStatus.Processing]: 'bg-saffron-100 text-saffron-800',
  [OrderStatus.Shipped]: 'bg-cardamom-100 text-cardamom-700',
  [OrderStatus.Delivered]: 'bg-cardamom-500 text-white',
  [OrderStatus.Cancelled]: 'bg-chilli-100 text-chilli-700',
  [OrderStatus.Refunded]: 'bg-chilli-100 text-chilli-700',
}

export function OrderStatusPill({ status }: { status: number }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold ${
        TONES[status] ?? 'bg-ink-100 text-ink-700'
      }`}
    >
      {ORDER_STATUS_LABEL[status] ?? 'Unknown'}
    </span>
  )
}

/** Payment status, kept visually quieter than the order status so the two do not compete. */
export function PaymentStatusPill({ status }: { status: number }) {
  const labels: Record<number, string> = {
    0: 'Payment pending',
    1: 'Authorised',
    2: 'Paid',
    3: 'Partly refunded',
    4: 'Refunded',
    5: 'Payment failed',
  }

  const tones: Record<number, string> = {
    0: 'border-ink-200 text-ink-500',
    1: 'border-saffron-200 text-saffron-700',
    2: 'border-cardamom-300 text-cardamom-700',
    3: 'border-chilli-200 text-chilli-600',
    4: 'border-chilli-200 text-chilli-600',
    5: 'border-chilli-300 text-chilli-700',
  }

  return (
    <span
      className={`inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium ${
        tones[status] ?? 'border-ink-200 text-ink-500'
      }`}
    >
      {labels[status] ?? 'Unknown'}
    </span>
  )
}
