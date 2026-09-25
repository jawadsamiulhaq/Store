import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, qs } from '../../lib/api'
import { Image } from '../../ui/Image'
import { Button, ButtonLink, EmptyState } from '../../ui/primitives'
import { OrderStatusPill } from '../../features/orders/OrderStatusPill'
import { formatDate, formatPrice } from '../../lib/format'
import type { OrderSummary, Paged } from '../../lib/types'

export default function OrdersPage() {
  const [page, setPage] = useState(1)

  const { data, isLoading } = useQuery({
    queryKey: ['orders', page],
    queryFn: () => api.get<Paged<OrderSummary>>(`/orders${qs({ page, pageSize: 10 })}`),
  })

  if (isLoading) {
    return (
      <div className="space-y-3" aria-busy="true">
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="skeleton h-24 w-full" />
        ))}
      </div>
    )
  }

  if (!data || data.items.length === 0) {
    return (
      <EmptyState
        title="No orders yet"
        description="Once you place an order it will appear here, with live tracking."
        action={<ButtonLink to="/shop">Start shopping</ButtonLink>}
      />
    )
  }

  return (
    <div>
      <ul className="space-y-3">
        {data.items.map((order) => (
          <li key={order.id}>
            <Link
              to={`/account/orders/${order.id}`}
              className="card-surface lift-on-hover flex items-center gap-4 p-4"
            >
              <Image
                src={order.firstItemImage}
                alt=""
                width={56}
                height={56}
                className="h-14 w-14 shrink-0 rounded-lg"
              />

              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-mono text-sm font-semibold text-ink-900">
                    {order.orderNumber}
                  </span>
                  <OrderStatusPill status={order.status} />
                </div>
                <p className="mt-1 text-xs text-ink-500">
                  {formatDate(order.placedAt)} · {order.itemCount} item
                  {order.itemCount === 1 ? '' : 's'}
                </p>
              </div>

              <div className="shrink-0 text-right">
                <p className="text-sm font-bold text-ink-900">{formatPrice(order.grandTotal)}</p>
                <span className="text-xs text-saffron-600">View →</span>
              </div>
            </Link>
          </li>
        ))}
      </ul>

      {data.totalPages > 1 && (
        <div className="mt-6 flex items-center justify-between">
          <Button
            variant="outline"
            size="sm"
            disabled={!data.hasPrevious}
            onClick={() => setPage((value) => value - 1)}
          >
            Previous
          </Button>

          <span className="text-sm text-ink-500">
            Page {data.page} of {data.totalPages}
          </span>

          <Button
            variant="outline"
            size="sm"
            disabled={!data.hasNext}
            onClick={() => setPage((value) => value + 1)}
          >
            Next
          </Button>
        </div>
      )}
    </div>
  )
}
