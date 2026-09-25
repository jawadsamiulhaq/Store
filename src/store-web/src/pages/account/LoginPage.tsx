import { useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Field, Input } from '../../ui/primitives'
import { useAuth } from '../../app/providers/AuthProvider'

export default function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { login, isAuthenticated } = useAuth()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Where the user was headed before the guard bounced them here.
  const returnTo = (location.state as { from?: string } | null)?.from ?? '/account'

  // Someone already signed in has no business on this page — send them on.
  useEffect(() => {
    if (isAuthenticated) {
      navigate(returnTo, { replace: true })
    }
  }, [isAuthenticated, navigate, returnTo])

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      await login(email.trim(), password)

      /*
        The guest cart is merged into the customer's cart server-side at login, so the cached
        copy the header is showing is now stale. Invalidating rather than clearing means the
        badge updates to the merged total instead of flashing empty.
      */
      await queryClient.invalidateQueries({ queryKey: ['cart'] })
      await queryClient.invalidateQueries({ queryKey: ['wishlist'] })

      navigate(returnTo, { replace: true })
    } catch (caught) {
      setError((caught as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="mx-auto flex max-w-md flex-col px-4 py-12 sm:px-6">
      <h1 className="text-2xl font-bold tracking-tight text-ink-900">Sign in</h1>
      <p className="mt-1.5 text-sm text-ink-500">
        New here?{' '}
        <Link to="/register" className="font-medium text-saffron-600 hover:text-saffron-700">
          Create an account
        </Link>
      </p>

      <form onSubmit={submit} className="card-surface mt-6 p-5">
        {error && (
          <div className="mb-4">
            <Alert tone="error">{error}</Alert>
          </div>
        )}

        <Field label="Email" htmlFor="email" required>
          <Input
            id="email"
            type="email"
            inputMode="email"
            autoComplete="email"
            // The first field of a sign-in form is the right place to take focus — it is
            // unambiguously what the user came here to do.
            autoFocus
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </Field>

        <Field label="Password" htmlFor="password" required>
          <Input
            id="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </Field>

        <Button type="submit" size="lg" fullWidth loading={busy} disabled={busy}>
          Sign in
        </Button>

        <p className="mt-4 text-center text-xs text-ink-400">
          Forgotten your password? Contact the shop and we will help you reset it.
        </p>
      </form>

      <p className="mt-6 text-center text-xs leading-relaxed text-ink-400">
        You can also{' '}
        <Link to="/track" className="underline hover:text-ink-600">
          track an order
        </Link>{' '}
        without an account, using your order number and email.
      </p>
    </div>
  )
}
