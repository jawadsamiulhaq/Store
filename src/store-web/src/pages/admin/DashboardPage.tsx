import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { Image } from '../../ui/Image'
import { PageHeader } from '../../features/admin/AdminTable'
import { OrderStatusPill } from '../../features/orders/OrderStatusPill'
import { formatDate, formatPrice } from '../../lib/format'

interface Dashboard {
  revenueToday: number
  revenueThisMonth: number
  revenueAllTime: number
  ordersToday: number
  ordersThisMonth: number
  pendingOrders: number
  processingOrders: number
  totalProducts: number
  activeProducts: number
  outOfStockCount: number
  lowStockCount: number
  totalCustomers: number
  newCustomersThisMonth: number
  pendingReviews: number
  averageOrderValue: number
  salesTrend: { date: string; revenue: number; orders: number }[]
  topProducts: {
    productId: string
    name: string
    slug: string
    thumbnailUrl?: string
    unitsSold: number
    revenue: number
  }[]
  recentOrders: {
    id: string
    orderNumber: string
    customerName: string
    grandTotal: number
    status: number
    placedAt: string
  }[]
}

export default function DashboardPage() {
  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'dashboard'],
    queryFn: () => api.get<Dashboard>('/admin/reports/dashboard'),
    // Refreshed on an interval so a shop watching the dashboard during trading sees new orders
    // without reloading, but not so often that it becomes a load source of its own.
    refetchInterval: 60_000,
  })

  if (isLoading || !data) {
    return (
      <div aria-busy="true">
        <PageHeader title="Dashboard" />
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 8 }, (_, index) => (
            <div key={index} className="skeleton h-24 w-full" />
          ))}
        </div>
      </div>
    )
  }

  return (
    <div>
      <PageHeader title="Dashboard" description="How the shop is trading right now." />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Revenue today" value={formatPrice(data.revenueToday)} sub={`${data.ordersToday} orders`} />
        <Stat
          label="Revenue this month"
          value={formatPrice(data.revenueThisMonth)}
          sub={`${data.ordersThisMonth} orders`}
        />
        <Stat label="Average order" value={formatPrice(data.averageOrderValue)} sub="All time" />
        <Stat
          label="Needs attention"
          value={String(data.pendingOrders + data.processingOrders)}
          sub={`${data.pendingOrders} pending · ${data.processingOrders} in progress`}
          tone={data.pendingOrders > 0 ? 'warning' : 'neutral'}
          href="/admin/orders"
        />
      </div>

      <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Products" value={String(data.activeProducts)} sub={`${data.totalProducts} total`} href="/admin/products" />
        <Stat
          label="Out of stock"
          value={String(data.outOfStockCount)}
          sub="Active products"
          tone={data.outOfStockCount > 0 ? 'danger' : 'neutral'}
          href="/admin/inventory?outOfStock=true"
        />
        <Stat
          label="Low stock"
          value={String(data.lowStockCount)}
          sub="At or below threshold"
          tone={data.lowStockCount > 0 ? 'warning' : 'neutral'}
          href="/admin/inventory?lowStock=true"
        />
        <Stat
          label="Reviews to moderate"
          value={String(data.pendingReviews)}
          sub={`${data.totalCustomers} customers`}
          tone={data.pendingReviews > 0 ? 'warning' : 'neutral'}
          href="/admin/reviews"
        />
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-3">
        <section className="card-surface p-5 lg:col-span-2">
          <h2 className="font-display text-base font-bold text-ink-900">Last 30 days</h2>
          <SalesChart points={data.salesTrend} />
        </section>

        <section className="card-surface p-5">
          <h2 className="font-display text-base font-bold text-ink-900">Best sellers this month</h2>

          {data.topProducts.length === 0 ? (
            <p className="mt-4 text-sm text-ink-400">No sales yet this month.</p>
          ) : (
            <ul className="mt-4 space-y-3">
              {data.topProducts.map((product) => (
                <li key={product.productId} className="flex items-center gap-3">
                  <Image src={product.thumbnailUrl} alt="" width={36} height={36} className="h-9 w-9 shrink-0 rounded-lg" />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-xs text-ink-700">{product.name}</p>
                    <p className="text-[11px] text-ink-400">{product.unitsSold} sold</p>
                  </div>
                  <span className="shrink-0 text-xs font-semibold text-ink-800">
                    {formatPrice(product.revenue)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <section className="card-surface mt-4 p-5">
        <div className="flex items-center justify-between">
          <h2 className="font-display text-base font-bold text-ink-900">Recent orders</h2>
          <Link to="/admin/orders" className="text-xs font-medium text-saffron-600 hover:text-saffron-700">
            View all →
          </Link>
        </div>

        {data.recentOrders.length === 0 ? (
          <p className="mt-4 text-sm text-ink-400">No orders yet.</p>
        ) : (
          <ul className="mt-4 divide-y divide-ink-50">
            {data.recentOrders.map((order) => (
              <li key={order.id}>
                <Link
                  to={`/admin/orders?search=${order.orderNumber}`}
                  className="flex items-center gap-3 py-2.5 transition-colors hover:bg-ink-50"
                >
                  <span className="font-mono text-xs font-semibold text-ink-800">{order.orderNumber}</span>
                  <span className="min-w-0 flex-1 truncate text-xs text-ink-500">{order.customerName}</span>
                  <OrderStatusPill status={order.status} />
                  <span className="shrink-0 text-xs font-semibold text-ink-900">
                    {formatPrice(order.grandTotal)}
                  </span>
                  <span className="hidden shrink-0 text-[11px] text-ink-400 sm:inline">
                    {formatDate(order.placedAt)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

function Stat({
  label,
  value,
  sub,
  tone = 'neutral',
  href,
}: {
  label: string
  value: string
  sub?: string
  tone?: 'neutral' | 'warning' | 'danger'
  href?: string
}) {
  const tones = {
    neutral: 'text-ink-900',
    warning: 'text-saffron-600',
    danger: 'text-chilli-600',
  } as const

  const body = (
    <div className="card-surface p-4">
      <p className="text-xs font-medium uppercase tracking-wide text-ink-400">{label}</p>
      <p className={`mt-1.5 text-2xl font-bold tracking-tight ${tones[tone]}`}>{value}</p>
      {sub && <p className="mt-0.5 text-xs text-ink-400">{sub}</p>}
    </div>
  )

  return href ? (
    <Link to={href} className="block transition-transform duration-150 hover:-translate-y-0.5">
      {body}
    </Link>
  ) : (
    body
  )
}

/**
 * Sales sparkline, drawn as inline SVG.
 *
 * No charting library. A dependency like Recharts is ~90 kB gzipped, and this is one line on one
 * screen — the whole point of the code-splitting work is not to undo it for a single chart.
 */
function SalesChart({ points }: { points: { date: string; revenue: number; orders: number }[] }) {
  if (points.length === 0) {
    return <p className="mt-4 text-sm text-ink-400">No data yet.</p>
  }

  const width = 600
  const height = 160
  const max = Math.max(...points.map((point) => point.revenue), 1)

  const coords = points.map((point, index) => ({
    x: (index / Math.max(1, points.length - 1)) * width,
    y: height - (point.revenue / max) * (height - 12),
    ...point,
  }))

  const line = coords.map((c, i) => `${i === 0 ? 'M' : 'L'}${c.x.toFixed(1)},${c.y.toFixed(1)}`).join(' ')
  const area = `${line} L${width},${height} L0,${height} Z`
  const totalRevenue = points.reduce((sum, point) => sum + point.revenue, 0)

  return (
    <div className="mt-4">
      <p className="text-2xl font-bold tracking-tight text-ink-900">{formatPrice(totalRevenue)}</p>
      <p className="text-xs text-ink-400">
        {points.reduce((sum, point) => sum + point.orders, 0)} orders over 30 days
      </p>

      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="mt-3 h-40 w-full"
        preserveAspectRatio="none"
        role="img"
        aria-label={`Revenue over the last 30 days, totalling ${formatPrice(totalRevenue)}`}
      >
        <defs>
          <linearGradient id="salesFill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--color-saffron-400)" stopOpacity="0.28" />
            <stop offset="100%" stopColor="var(--color-saffron-400)" stopOpacity="0" />
          </linearGradient>
        </defs>

        <path d={area} fill="url(#salesFill)" />
        <path
          d={line}
          fill="none"
          stroke="var(--color-saffron-500)"
          strokeWidth="2"
          strokeLinejoin="round"
          strokeLinecap="round"
          // preserveAspectRatio="none" stretches strokes horizontally; this keeps the line an
          // even weight regardless of the container's width.
          vectorEffect="non-scaling-stroke"
        />
      </svg>

      <div className="mt-1 flex justify-between text-[10px] text-ink-400">
        <span>{formatDate(points[0].date)}</span>
        <span>{formatDate(points.at(-1)!.date)}</span>
      </div>
    </div>
  )
}
