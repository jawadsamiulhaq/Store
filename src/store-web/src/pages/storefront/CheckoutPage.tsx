import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Image } from '../../ui/Image'
import { Alert, Button, ButtonLink, EmptyState, Field, Input, Select, Textarea } from '../../ui/primitives'
import { useAuth } from '../../app/providers/AuthProvider'
import { useStore } from '../../app/providers/StoreProvider'
import { formatPrice } from '../../lib/format'
import type { Address, CheckoutSummary, OrderDetail } from '../../lib/types'

/** Hong Kong's three territories. The meaningful shipping distinction here — not a postcode. */
const REGIONS = ['Kowloon', 'Hong Kong Island', 'New Territories'] as const

interface FormState {
  email: string
  phone: string
  fullName: string
  line1: string
  line2: string
  district: string
  region: string
  customerNote: string
  saveAddress: boolean
}

export default function CheckoutPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { isAuthenticated, user } = useAuth()
  const { settingBool } = useStore()

  const [form, setForm] = useState<FormState>({
    email: '',
    phone: '',
    fullName: '',
    line1: '',
    line2: '',
    district: '',
    region: 'Kowloon',
    customerNote: '',
    saveAddress: true,
  })

  const [shippingMethodId, setShippingMethodId] = useState<string | null>(null)
  const [paymentMethod, setPaymentMethod] = useState(0)
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({})

  // Saved addresses, for signed-in customers only.
  const { data: addresses } = useQuery({
    queryKey: ['addresses'],
    queryFn: () => api.get<Address[]>('/addresses'),
    enabled: isAuthenticated,
  })

  /*
    The summary is re-priced by the server whenever the region or the chosen method changes,
    because shipping depends on both. The client never computes a total itself — it displays
    what the server says, and the server recomputes again at placement.
  */
  const { data: summary, isLoading } = useQuery({
    queryKey: ['checkout', 'summary', form.region, shippingMethodId],
    queryFn: () =>
      api.get<CheckoutSummary>(
        `/checkout/summary${qs({ region: form.region, shippingMethodId: shippingMethodId ?? undefined })}`,
      ),
    staleTime: 0,
  })

  // Prefill from the account and its default address, so a returning customer types nothing.
  useEffect(() => {
    if (!user) return

    setForm((current) => ({
      ...current,
      email: current.email || user.email,
      phone: current.phone || (user.phone ?? ''),
      fullName: current.fullName || user.fullName,
    }))
  }, [user])

  useEffect(() => {
    const preferred = addresses?.find((address) => address.isDefaultShipping) ?? addresses?.[0]

    if (!preferred) return

    setForm((current) => ({
      ...current,
      fullName: current.fullName || preferred.fullName,
      phone: current.phone || preferred.phone,
      line1: current.line1 || preferred.line1,
      line2: current.line2 || (preferred.line2 ?? ''),
      district: current.district || (preferred.district ?? ''),
      region: preferred.region ?? current.region,
    }))
  }, [addresses])

  // Default to the first (cheapest) option once quotes arrive.
  useEffect(() => {
    if (!shippingMethodId && summary?.shippingOptions.length) {
      setShippingMethodId(summary.shippingOptions[0].methodId)
    }
  }, [summary, shippingMethodId])

  const placeOrder = useMutation({
    mutationFn: (payload: unknown) => api.post<OrderDetail>('/checkout', payload),
    onSuccess: (order) => {
      // The cart was consumed server-side; clearing it locally keeps the header badge honest.
      queryClient.setQueryData(['cart'], undefined)
      void queryClient.invalidateQueries({ queryKey: ['cart'] })
      navigate(`/order-confirmation/${order.id}`, { replace: true, state: { order } })
    },
  })

  const guestCheckoutAllowed = settingBool('checkout.guest-enabled', true)

  const selectedShipping = useMemo(
    () => summary?.shippingOptions.find((option) => option.methodId === shippingMethodId),
    [summary, shippingMethodId],
  )

  function validate(): boolean {
    const next: Partial<Record<keyof FormState, string>> = {}

    if (!form.email.trim()) next.email = 'We need an email to send your receipt.'
    else if (!/^\S+@\S+\.\S+$/.test(form.email)) next.email = 'That does not look like an email address.'

    if (!form.phone.trim()) next.phone = 'A phone number helps the driver reach you.'
    if (!form.fullName.trim()) next.fullName = 'Who should we deliver to?'
    if (!form.line1.trim()) next.line1 = 'We need a street address.'
    if (!shippingMethodId) next.region = 'Choose a delivery option.'

    setErrors(next)
    return Object.keys(next).length === 0
  }

  function submit(event: React.FormEvent) {
    event.preventDefault()

    if (!validate()) {
      // Move focus to the first problem rather than leaving the user to hunt for it.
      document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
      return
    }

    placeOrder.mutate({
      email: form.email.trim(),
      phone: form.phone.trim(),
      shippingAddress: {
        fullName: form.fullName.trim(),
        phone: form.phone.trim(),
        line1: form.line1.trim(),
        line2: form.line2.trim() || undefined,
        district: form.district.trim() || undefined,
        city: 'Hong Kong',
        region: form.region,
        countryCode: 'HK',
      },
      billingAddress: null,
      shippingMethodId,
      paymentMethod,
      couponCode: summary?.coupon?.code,
      customerNote: form.customerNote.trim() || undefined,
      saveAddress: isAuthenticated && form.saveAddress,
    })
  }

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }))
    setErrors((current) => ({ ...current, [key]: undefined }))
  }

  if (isLoading) {
    return (
      <div className="mx-auto max-w-5xl px-4 py-10 sm:px-6" aria-busy="true">
        <div className="skeleton h-8 w-40" />
        <div className="mt-6 skeleton h-96 w-full" />
      </div>
    )
  }

  if (!summary || summary.items.length === 0) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6">
        <EmptyState
          title="There is nothing to check out"
          description="Your cart is empty."
          action={<ButtonLink to="/shop">Browse products</ButtonLink>}
        />
      </div>
    )
  }

  if (!isAuthenticated && !guestCheckoutAllowed) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6">
        <EmptyState
          title="Please sign in to check out"
          description="This shop requires an account to place an order."
          action={<ButtonLink to="/login">Sign in</ButtonLink>}
        />
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <h1 className="text-2xl font-bold tracking-tight text-ink-900 sm:text-3xl">Checkout</h1>

      {/* Server-side blockers: out of stock, below minimum, coupon no longer valid. */}
      {summary.blockers.length > 0 && (
        <div className="mt-4 space-y-2">
          {summary.blockers.map((blocker) => (
            <Alert key={blocker} tone="error">
              {blocker}
            </Alert>
          ))}
        </div>
      )}

      {!isAuthenticated && (
        <div className="mt-4">
          <Alert tone="info">
            Checking out as a guest.{' '}
            <Link to="/login" className="font-medium underline">
              Sign in
            </Link>{' '}
            to save your details and track this order from your account.
          </Alert>
        </div>
      )}

      <form onSubmit={submit} className="mt-6 grid gap-8 lg:grid-cols-[1fr_22rem]">
        <div className="space-y-8">
          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Contact</h2>
            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              <Field label="Email" htmlFor="email" required error={errors.email}>
                <Input
                  id="email"
                  type="email"
                  inputMode="email"
                  autoComplete="email"
                  value={form.email}
                  invalid={Boolean(errors.email)}
                  onChange={(event) => update('email', event.target.value)}
                />
              </Field>

              <Field label="Phone" htmlFor="phone" required error={errors.phone}>
                <Input
                  id="phone"
                  type="tel"
                  inputMode="tel"
                  autoComplete="tel"
                  placeholder="+852 …"
                  value={form.phone}
                  invalid={Boolean(errors.phone)}
                  onChange={(event) => update('phone', event.target.value)}
                />
              </Field>
            </div>
          </section>

          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Delivery address</h2>

            <div className="mt-4 space-y-3">
              <Field label="Recipient name" htmlFor="fullName" required error={errors.fullName}>
                <Input
                  id="fullName"
                  autoComplete="name"
                  value={form.fullName}
                  invalid={Boolean(errors.fullName)}
                  onChange={(event) => update('fullName', event.target.value)}
                />
              </Field>

              <Field label="Flat, floor and building" htmlFor="line1" required error={errors.line1}>
                <Input
                  id="line1"
                  autoComplete="address-line1"
                  placeholder="Flat 8B, 12 Clear Water Bay Road"
                  value={form.line1}
                  invalid={Boolean(errors.line1)}
                  onChange={(event) => update('line1', event.target.value)}
                />
              </Field>

              <Field label="Estate or street (optional)" htmlFor="line2">
                <Input
                  id="line2"
                  autoComplete="address-line2"
                  value={form.line2}
                  onChange={(event) => update('line2', event.target.value)}
                />
              </Field>

              <div className="grid gap-3 sm:grid-cols-2">
                <Field label="District" htmlFor="district" hint="e.g. Wong Tai Sin">
                  <Input
                    id="district"
                    autoComplete="address-level2"
                    value={form.district}
                    onChange={(event) => update('district', event.target.value)}
                  />
                </Field>

                <Field label="Region" htmlFor="region" required>
                  <Select
                    id="region"
                    value={form.region}
                    onChange={(event) => {
                      update('region', event.target.value)
                      // Rates differ per territory, so the chosen method is cleared and re-quoted.
                      setShippingMethodId(null)
                    }}
                  >
                    {REGIONS.map((region) => (
                      <option key={region} value={region}>
                        {region}
                      </option>
                    ))}
                  </Select>
                </Field>
              </div>

              {/* No postcode field: Hong Kong has no postal codes, and asking for one is a
                  well-known way to make an address form unfillable. */}

              {isAuthenticated && (
                <label className="flex items-center gap-2 text-sm text-ink-600">
                  <input
                    type="checkbox"
                    checked={form.saveAddress}
                    onChange={(event) => update('saveAddress', event.target.checked)}
                    className="h-4 w-4 rounded border-ink-300 text-saffron-500"
                  />
                  Save this address for next time
                </label>
              )}
            </div>
          </section>

          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Delivery option</h2>

            <div className="mt-4 space-y-2">
              {summary.shippingOptions.map((option) => (
                <label
                  key={option.methodId}
                  className={`flex cursor-pointer items-center gap-3 rounded-xl border p-3.5 transition-colors ${
                    shippingMethodId === option.methodId
                      ? 'border-saffron-500 bg-saffron-50'
                      : 'border-ink-200 hover:border-ink-300'
                  }`}
                >
                  <input
                    type="radio"
                    name="shipping"
                    value={option.methodId}
                    checked={shippingMethodId === option.methodId}
                    onChange={() => setShippingMethodId(option.methodId)}
                    className="h-4 w-4 text-saffron-500"
                  />
                  <span className="flex-1">
                    <span className="block text-sm font-medium text-ink-800">{option.name}</span>
                    {option.description && (
                      <span className="block text-xs text-ink-500">{option.description}</span>
                    )}
                    <span className="block text-xs text-ink-400">
                      {option.estimatedDaysMin === 0
                        ? 'Today or tomorrow'
                        : `${option.estimatedDaysMin}–${option.estimatedDaysMax} days`}
                    </span>
                  </span>
                  <span className="text-sm font-semibold text-ink-900">
                    {option.isFree ? 'Free' : formatPrice(option.rate)}
                  </span>
                </label>
              ))}
            </div>
          </section>

          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Payment</h2>

            <div className="mt-4 space-y-2">
              {[
                { value: 0, label: 'Cash on delivery', hint: 'Pay the driver when your order arrives.' },
                { value: 1, label: 'Bank transfer', hint: 'We will send account details with your confirmation.' },
                { value: 3, label: 'FPS / e-wallet', hint: 'Pay by FPS after placing the order.' },
              ].map((method) => (
                <label
                  key={method.value}
                  className={`flex cursor-pointer items-center gap-3 rounded-xl border p-3.5 transition-colors ${
                    paymentMethod === method.value
                      ? 'border-saffron-500 bg-saffron-50'
                      : 'border-ink-200 hover:border-ink-300'
                  }`}
                >
                  <input
                    type="radio"
                    name="payment"
                    checked={paymentMethod === method.value}
                    onChange={() => setPaymentMethod(method.value)}
                    className="h-4 w-4 text-saffron-500"
                  />
                  <span>
                    <span className="block text-sm font-medium text-ink-800">{method.label}</span>
                    <span className="block text-xs text-ink-500">{method.hint}</span>
                  </span>
                </label>
              ))}
            </div>

            <div className="mt-4">
              <Field label="Delivery notes (optional)" htmlFor="note">
                <Textarea
                  id="note"
                  rows={3}
                  placeholder="Leave with the guard, call on arrival…"
                  value={form.customerNote}
                  onChange={(event) => update('customerNote', event.target.value)}
                />
              </Field>
            </div>
          </section>
        </div>

        {/* ---- Order summary ---- */}
        <aside className="lg:sticky lg:top-36 lg:self-start">
          <div className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Your order</h2>

            <ul className="mt-4 max-h-64 space-y-3 overflow-y-auto pr-1">
              {summary.items.map((item) => (
                <li key={item.id} className="flex gap-3">
                  <div className="relative shrink-0">
                    <Image src={item.imageUrl} alt="" width={48} height={48} className="h-12 w-12 rounded-lg" />
                    <span className="absolute -right-1.5 -top-1.5 grid h-5 min-w-5 place-items-center rounded-full bg-ink-800 px-1 text-[10px] font-bold text-paper">
                      {item.quantity}
                    </span>
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="line-clamp-2 text-xs leading-snug text-ink-700">{item.productName}</p>
                    {item.variantName && <p className="text-[11px] text-ink-400">{item.variantName}</p>}
                  </div>
                  <span className="shrink-0 text-xs font-semibold text-ink-800">
                    {formatPrice(item.lineTotal)}
                  </span>
                </li>
              ))}
            </ul>

            <dl className="mt-4 space-y-2 border-t border-ink-100 pt-4 text-sm">
              <div className="flex justify-between">
                <dt className="text-ink-600">Subtotal</dt>
                <dd className="font-medium text-ink-800">{formatPrice(summary.subtotal)}</dd>
              </div>

              {summary.discountTotal > 0 && (
                <div className="flex justify-between">
                  <dt className="text-ink-600">Discount {summary.coupon && `(${summary.coupon.code})`}</dt>
                  <dd className="font-medium text-cardamom-600">−{formatPrice(summary.discountTotal)}</dd>
                </div>
              )}

              <div className="flex justify-between">
                <dt className="text-ink-600">Delivery</dt>
                <dd className="font-medium text-ink-800">
                  {selectedShipping?.isFree ? 'Free' : formatPrice(summary.shippingTotal)}
                </dd>
              </div>

              <div className="flex items-baseline justify-between border-t border-ink-100 pt-2.5">
                <dt className="font-semibold text-ink-900">Total</dt>
                <dd className="text-xl font-bold text-ink-900">{formatPrice(summary.grandTotal)}</dd>
              </div>
            </dl>

            {placeOrder.isError && (
              <div className="mt-4">
                <Alert tone="error" title="We could not place your order">
                  {(placeOrder.error as Error).message}
                </Alert>
              </div>
            )}

            <div className="mt-5">
              <Button
                type="submit"
                size="lg"
                fullWidth
                loading={placeOrder.isPending}
                disabled={summary.blockers.length > 0 || placeOrder.isPending}
              >
                Place order · {formatPrice(summary.grandTotal)}
              </Button>
            </div>

            <p className="mt-3 text-center text-xs leading-relaxed text-ink-400">
              By placing this order you agree to our{' '}
              <Link to="/page/terms" className="underline hover:text-ink-600">
                terms
              </Link>{' '}
              and{' '}
              <Link to="/page/privacy" className="underline hover:text-ink-600">
                privacy policy
              </Link>
              .
            </p>
          </div>
        </aside>
      </form>
    </div>
  )
}
