import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Field, Input } from '../../ui/primitives'
import { useAuth } from '../../app/providers/AuthProvider'

/** Mirrors the server's Identity policy, so the client never promises something the API rejects. */
const MIN_PASSWORD_LENGTH = 10

export default function RegisterPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { register, isAuthenticated } = useAuth()

  const [form, setForm] = useState({
    firstName: '',
    lastName: '',
    email: '',
    phone: '',
    password: '',
    acceptsMarketing: false,
  })

  const [errors, setErrors] = useState<Record<string, string>>({})
  const [serverError, setServerError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (isAuthenticated) {
      navigate('/account', { replace: true })
    }
  }, [isAuthenticated, navigate])

  function update<K extends keyof typeof form>(key: K, value: (typeof form)[K]) {
    setForm((current) => ({ ...current, [key]: value }))
    setErrors((current) => ({ ...current, [key]: '' }))
  }

  function validate(): boolean {
    const next: Record<string, string> = {}

    if (!form.firstName.trim()) next.firstName = 'We need a first name.'
    if (!form.lastName.trim()) next.lastName = 'We need a last name.'

    if (!form.email.trim()) next.email = 'An email address is required.'
    else if (!/^\S+@\S+\.\S+$/.test(form.email)) next.email = 'That does not look like an email address.'

    if (form.password.length < MIN_PASSWORD_LENGTH) {
      next.password = `Use at least ${MIN_PASSWORD_LENGTH} characters.`
    } else if (!/\d/.test(form.password)) {
      next.password = 'Include at least one number.'
    } else if (!/[a-z]/.test(form.password)) {
      next.password = 'Include at least one lowercase letter.'
    }

    setErrors(next)
    return Object.keys(next).length === 0
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setServerError(null)

    if (!validate()) {
      document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
      return
    }

    setBusy(true)

    try {
      await register({
        email: form.email.trim(),
        password: form.password,
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        phone: form.phone.trim() || undefined,
        acceptsMarketing: form.acceptsMarketing,
      })

      // A guest cart is adopted by the new account server-side, so the cached copy is stale.
      await queryClient.invalidateQueries({ queryKey: ['cart'] })

      navigate('/account', { replace: true })
    } catch (caught) {
      setServerError((caught as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="mx-auto flex max-w-md flex-col px-4 py-12 sm:px-6">
      <h1 className="text-2xl font-bold tracking-tight text-ink-900">Create an account</h1>
      <p className="mt-1.5 text-sm text-ink-500">
        Already have one?{' '}
        <Link to="/login" className="font-medium text-saffron-600 hover:text-saffron-700">
          Sign in
        </Link>
      </p>

      <form onSubmit={submit} className="card-surface mt-6 p-5">
        {serverError && (
          <div className="mb-4">
            <Alert tone="error">{serverError}</Alert>
          </div>
        )}

        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="First name" htmlFor="firstName" required error={errors.firstName}>
            <Input
              id="firstName"
              autoComplete="given-name"
              autoFocus
              value={form.firstName}
              invalid={Boolean(errors.firstName)}
              onChange={(event) => update('firstName', event.target.value)}
            />
          </Field>

          <Field label="Last name" htmlFor="lastName" required error={errors.lastName}>
            <Input
              id="lastName"
              autoComplete="family-name"
              value={form.lastName}
              invalid={Boolean(errors.lastName)}
              onChange={(event) => update('lastName', event.target.value)}
            />
          </Field>
        </div>

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

        <Field label="Phone" htmlFor="phone" hint="Optional — helps the driver reach you">
          <Input
            id="phone"
            type="tel"
            inputMode="tel"
            autoComplete="tel"
            placeholder="+852 …"
            value={form.phone}
            onChange={(event) => update('phone', event.target.value)}
          />
        </Field>

        <Field
          label="Password"
          htmlFor="password"
          required
          error={errors.password}
          hint={`At least ${MIN_PASSWORD_LENGTH} characters, with a number`}
        >
          <Input
            id="password"
            type="password"
            autoComplete="new-password"
            value={form.password}
            invalid={Boolean(errors.password)}
            onChange={(event) => update('password', event.target.value)}
          />
        </Field>

        {/* Opt-in, unchecked by default. Pre-ticking a marketing box is not consent. */}
        <label className="mb-4 flex items-start gap-2 text-sm text-ink-600">
          <input
            type="checkbox"
            checked={form.acceptsMarketing}
            onChange={(event) => update('acceptsMarketing', event.target.checked)}
            className="mt-0.5 h-4 w-4 rounded border-ink-300 text-saffron-500"
          />
          <span>Email me occasional offers and new arrivals.</span>
        </label>

        <Button type="submit" size="lg" fullWidth loading={busy} disabled={busy}>
          Create account
        </Button>

        <p className="mt-4 text-center text-xs leading-relaxed text-ink-400">
          By creating an account you agree to our{' '}
          <Link to="/page/terms" className="underline hover:text-ink-600">
            terms
          </Link>{' '}
          and{' '}
          <Link to="/page/privacy" className="underline hover:text-ink-600">
            privacy policy
          </Link>
          .
        </p>
      </form>
    </div>
  )
}
