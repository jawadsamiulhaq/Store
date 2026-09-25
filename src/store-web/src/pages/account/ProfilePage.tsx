import { useEffect, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { Alert, Button, Field, Input } from '../../ui/primitives'
import { useAuth } from '../../app/providers/AuthProvider'
import type { CurrentUser } from '../../lib/types'

export default function ProfilePage() {
  const { user, refreshUser } = useAuth()

  const [profile, setProfile] = useState({ firstName: '', lastName: '', phone: '' })
  const [passwords, setPasswords] = useState({ current: '', next: '', confirm: '' })
  const [passwordError, setPasswordError] = useState<string | null>(null)
  const [profileSaved, setProfileSaved] = useState(false)
  const [passwordSaved, setPasswordSaved] = useState(false)

  useEffect(() => {
    if (user) {
      setProfile({
        firstName: user.firstName,
        lastName: user.lastName,
        phone: user.phone ?? '',
      })
    }
  }, [user])

  const saveProfile = useMutation({
    mutationFn: () =>
      api.put<CurrentUser>('/auth/me', {
        firstName: profile.firstName.trim(),
        lastName: profile.lastName.trim(),
        phone: profile.phone.trim() || null,
        avatarUrl: user?.avatarUrl ?? null,
        preferredLanguage: user?.preferredLanguage ?? 'en',
      }),
    onSuccess: async () => {
      await refreshUser()
      setProfileSaved(true)
      window.setTimeout(() => setProfileSaved(false), 3000)
    },
  })

  const changePassword = useMutation({
    mutationFn: () =>
      api.post<void>('/auth/change-password', {
        currentPassword: passwords.current,
        newPassword: passwords.next,
      }),
    onSuccess: () => {
      setPasswords({ current: '', next: '', confirm: '' })
      setPasswordSaved(true)
      window.setTimeout(() => setPasswordSaved(false), 5000)
    },
  })

  function submitPassword(event: React.FormEvent) {
    event.preventDefault()
    setPasswordError(null)

    if (passwords.next !== passwords.confirm) {
      setPasswordError('The two new passwords do not match.')
      return
    }

    if (passwords.next.length < 10) {
      setPasswordError('Use at least 10 characters.')
      return
    }

    changePassword.mutate()
  }

  return (
    <div className="max-w-2xl space-y-6">
      <section className="card-surface p-5">
        <h2 className="font-display text-base font-bold text-ink-900">Your details</h2>

        <form
          onSubmit={(event) => {
            event.preventDefault()
            saveProfile.mutate()
          }}
          className="mt-4"
        >
          {profileSaved && (
            <div className="mb-4">
              <Alert tone="success">Your details have been saved.</Alert>
            </div>
          )}

          {saveProfile.isError && (
            <div className="mb-4">
              <Alert tone="error">{(saveProfile.error as Error).message}</Alert>
            </div>
          )}

          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="First name" htmlFor="firstName" required>
              <Input
                id="firstName"
                autoComplete="given-name"
                value={profile.firstName}
                onChange={(event) => setProfile((p) => ({ ...p, firstName: event.target.value }))}
              />
            </Field>

            <Field label="Last name" htmlFor="lastName" required>
              <Input
                id="lastName"
                autoComplete="family-name"
                value={profile.lastName}
                onChange={(event) => setProfile((p) => ({ ...p, lastName: event.target.value }))}
              />
            </Field>
          </div>

          <Field label="Phone" htmlFor="phone">
            <Input
              id="phone"
              type="tel"
              inputMode="tel"
              autoComplete="tel"
              value={profile.phone}
              onChange={(event) => setProfile((p) => ({ ...p, phone: event.target.value }))}
            />
          </Field>

          {/* Email is deliberately read-only: changing a sign-in identity needs verification, and
              an unverified change would lock the customer out of their own account. */}
          <Field label="Email" htmlFor="emailReadonly" hint="Contact us if you need to change this">
            <Input id="emailReadonly" value={user?.email ?? ''} disabled readOnly />
          </Field>

          <Button type="submit" loading={saveProfile.isPending}>
            Save changes
          </Button>
        </form>
      </section>

      <section className="card-surface p-5">
        <h2 className="font-display text-base font-bold text-ink-900">Change password</h2>
        <p className="mt-1 text-xs text-ink-500">
          Changing your password signs you out everywhere else.
        </p>

        <form onSubmit={submitPassword} className="mt-4">
          {passwordSaved && (
            <div className="mb-4">
              <Alert tone="success">
                Your password has been changed and other sessions have been signed out.
              </Alert>
            </div>
          )}

          {(passwordError || changePassword.isError) && (
            <div className="mb-4">
              <Alert tone="error">
                {passwordError ?? (changePassword.error as Error).message}
              </Alert>
            </div>
          )}

          <Field label="Current password" htmlFor="currentPassword" required>
            <Input
              id="currentPassword"
              type="password"
              autoComplete="current-password"
              value={passwords.current}
              onChange={(event) => setPasswords((p) => ({ ...p, current: event.target.value }))}
            />
          </Field>

          <Field label="New password" htmlFor="newPassword" required hint="At least 10 characters, with a number">
            <Input
              id="newPassword"
              type="password"
              autoComplete="new-password"
              value={passwords.next}
              onChange={(event) => setPasswords((p) => ({ ...p, next: event.target.value }))}
            />
          </Field>

          <Field label="Confirm new password" htmlFor="confirmPassword" required>
            <Input
              id="confirmPassword"
              type="password"
              autoComplete="new-password"
              value={passwords.confirm}
              onChange={(event) => setPasswords((p) => ({ ...p, confirm: event.target.value }))}
            />
          </Field>

          <Button
            type="submit"
            variant="outline"
            loading={changePassword.isPending}
            disabled={!passwords.current || !passwords.next}
          >
            Change password
          </Button>
        </form>
      </section>
    </div>
  )
}
