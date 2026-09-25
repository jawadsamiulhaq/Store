/**
 * Formatting helpers.
 *
 * `Intl` formatters are expensive to construct and cheap to reuse, so they are created once at
 * module scope rather than per render. In a grid of 24 product cards each showing two prices,
 * constructing a formatter per call is measurable work for no benefit.
 */

const currencyFormatter = new Intl.NumberFormat('en-HK', {
  style: 'currency',
  currency: 'HKD',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

/** Whole-dollar variant, for prices that happen to be round. */
const currencyWholeFormatter = new Intl.NumberFormat('en-HK', {
  style: 'currency',
  currency: 'HKD',
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
})

const dateFormatter = new Intl.DateTimeFormat('en-HK', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
})

const dateTimeFormatter = new Intl.DateTimeFormat('en-HK', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

/** Formats money. Drops the decimals when the amount is whole, which most grocery prices are. */
export function formatPrice(amount: number): string {
  return Number.isInteger(amount)
    ? currencyWholeFormatter.format(amount)
    : currencyFormatter.format(amount)
}

/** "HK$12 – HK$48" for a product sold in several pack sizes. */
export function formatPriceRange(min: number, max: number): string {
  return min === max ? formatPrice(min) : `${formatPrice(min)} – ${formatPrice(max)}`
}

export function formatDate(iso: string): string {
  return dateFormatter.format(new Date(iso))
}

export function formatDateTime(iso: string): string {
  return dateTimeFormatter.format(new Date(iso))
}

/** "2 days ago" — falls back to an absolute date past a week, where relative stops being useful. */
export function formatRelative(iso: string): string {
  const then = new Date(iso).getTime()
  const seconds = Math.round((Date.now() - then) / 1000)

  if (seconds < 60) return 'just now'
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`
  if (seconds < 86_400) return `${Math.floor(seconds / 3600)} hr ago`
  if (seconds < 604_800) return `${Math.floor(seconds / 86_400)} d ago`

  return formatDate(iso)
}

/** "500 g" / "1 kg" — the pack size shown beneath a variant. */
export function formatUnit(unit: string, value?: number): string {
  if (!value || unit === 'piece') {
    return unit === 'piece' ? '' : unit
  }

  return `${value % 1 === 0 ? value : value.toFixed(2)} ${unit}`
}

/** "HK$24.00 per kg" — lets a shopper compare a 500 g pack against a 1 kg one. */
export function formatPricePerUnit(pricePerUnit: number | undefined, unit: string): string | null {
  if (!pricePerUnit || unit === 'piece') {
    return null
  }

  return `${formatPrice(pricePerUnit)} per ${unit}`
}
