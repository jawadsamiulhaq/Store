import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { Alert, Badge, Button, Checkbox, Field, Input, Select } from '../../ui/primitives'
import { useAuth } from '../../app/providers/AuthProvider'
import type { PermissionModule, Role } from './RolesPanel'

interface UserOverride {
  permissionCode: string
  isGranted: boolean
}

/** `UserDetailDto`. */
interface UserDetail {
  id: string
  email: string
  firstName: string
  lastName: string
  phone?: string
  isActive: boolean
  emailConfirmed: boolean
  roles: string[]
  effectivePermissions: string[]
  overrides: UserOverride[]
  createdAt: string
  lastLoginAt?: string
}

/** Inherit / grant / deny, per permission. */
type OverrideState = 'inherit' | 'grant' | 'deny'

/**
 * Edits one user: profile, roles, and per-user permission overrides.
 *
 * Roles and overrides are two different mechanisms and are saved through two different endpoints,
 * so they are presented as two separate sections with their own save buttons rather than one
 * "Save" that silently does both. Roles are the normal way to grant capability; an override is
 * the exception for one person, and it is worth making that feel like the exception.
 */
export function UserEditor({ userId, onClose }: { userId: string; onClose: () => void }) {
  const queryClient = useQueryClient()
  const { can, user: actor } = useAuth()

  const canAssignRoles = can('users.assign-roles')
  const canManagePermissions = can('users.manage-permissions')

  const [profile, setProfile] = useState({ firstName: '', lastName: '', phone: '', isActive: true })
  const [roles, setRoles] = useState<string[]>([])
  const [overrides, setOverrides] = useState<Record<string, OverrideState>>({})
  const [showOverrides, setShowOverrides] = useState(false)

  const detail = useQuery({
    queryKey: ['admin', 'user', userId],
    queryFn: () => api.get<UserDetail>(`/admin/users/${userId}`),
  })

  const allRoles = useQuery({
    queryKey: ['admin', 'roles'],
    queryFn: () => api.get<Role[]>('/admin/roles'),
  })

  const catalogue = useQuery({
    queryKey: ['admin', 'permissions'],
    queryFn: () => api.get<PermissionModule[]>('/admin/permissions'),
    enabled: canManagePermissions,
  })

  // Keyed on the id, not the object: a window-focus refetch while someone is halfway through
  // retyping a name must not overwrite what they have typed. The parent remounts this component
  // per user, so loading once is the whole requirement.
  useEffect(() => {
    const loaded = detail.data
    if (!loaded) return

    setProfile({
      firstName: loaded.firstName,
      lastName: loaded.lastName,
      phone: loaded.phone ?? '',
      isActive: loaded.isActive,
    })

    setRoles(loaded.roles)

    setOverrides(
      Object.fromEntries(
        loaded.overrides.map((override) => [
          override.permissionCode,
          override.isGranted ? 'grant' : 'deny',
        ]),
      ),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [detail.data?.id])

  /**
   * What the user's roles alone would give them.
   *
   * Effective permissions already have the overrides applied, so subtracting them is what makes
   * "Inherit" mean something concrete on each row — otherwise a denied permission looks identical
   * to one the roles never granted.
   */
  const fromRoles = useMemo(() => {
    const loaded = detail.data
    if (!loaded) return new Set<string>()

    const effective = new Set(loaded.effectivePermissions)

    for (const override of loaded.overrides) {
      if (override.isGranted) effective.delete(override.permissionCode)
      else effective.add(override.permissionCode)
    }

    return effective
  }, [detail.data])

  const saveProfile = useMutation({
    mutationFn: () =>
      api.put<UserDetail>(`/admin/users/${userId}`, {
        firstName: profile.firstName.trim(),
        lastName: profile.lastName.trim(),
        phone: profile.phone.trim() || null,
        isActive: profile.isActive,
        // Sent only when the actor may change them. Sending the unchanged list would be harmless
        // (the server compares before acting) but sending null is the honest "not my business".
        roles: canAssignRoles ? roles : null,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users'] })
      await queryClient.invalidateQueries({ queryKey: ['admin', 'user', userId] })
      await queryClient.invalidateQueries({ queryKey: ['admin', 'roles'] })
    },
  })

  const savePermissions = useMutation({
    mutationFn: () =>
      api.put<void>(`/admin/users/${userId}/permissions`, {
        overrides: Object.entries(overrides)
          .filter(([, state]) => state !== 'inherit')
          .map(([permissionCode, state]) => ({ permissionCode, isGranted: state === 'grant' })),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'user', userId] })
    },
  })

  if (detail.isLoading) {
    return <div className="skeleton mb-5 h-80 w-full" />
  }

  if (detail.isError) {
    return (
      <div className="mb-5">
        <Alert tone="error">{(detail.error as Error).message}</Alert>
      </div>
    )
  }

  const loaded = detail.data!
  const overrideCount = Object.values(overrides).filter((state) => state !== 'inherit').length

  return (
    <section className="card-surface animate-fade-rise mb-5 p-5">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-display text-base font-bold text-ink-900">
            {loaded.firstName} {loaded.lastName}
          </h2>
          <p className="text-sm text-ink-500">{loaded.email}</p>
        </div>

        <div className="flex items-center gap-2">
          {loaded.roles.map((role) => (
            <Badge key={role} tone={role === 'System' ? 'warning' : 'neutral'}>
              {role}
            </Badge>
          ))}
          <Button variant="ghost" size="sm" onClick={onClose}>
            Close
          </Button>
        </div>
      </header>

      {saveProfile.isError && (
        <div className="mt-4">
          <Alert tone="error">{(saveProfile.error as Error).message}</Alert>
        </div>
      )}

      {/* ---- Profile ---- */}
      <div className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Field label="First name" htmlFor="user-first" required>
          <Input
            id="user-first"
            value={profile.firstName}
            onChange={(event) => setProfile((p) => ({ ...p, firstName: event.target.value }))}
          />
        </Field>

        <Field label="Last name" htmlFor="user-last" required>
          <Input
            id="user-last"
            value={profile.lastName}
            onChange={(event) => setProfile((p) => ({ ...p, lastName: event.target.value }))}
          />
        </Field>

        <Field label="Phone" htmlFor="user-phone">
          <Input
            id="user-phone"
            value={profile.phone}
            onChange={(event) => setProfile((p) => ({ ...p, phone: event.target.value }))}
          />
        </Field>

        <div className="pt-6">
          <Checkbox
            label="Active"
            hint="Inactive accounts cannot sign in"
            checked={profile.isActive}
            onChange={(event) => setProfile((p) => ({ ...p, isActive: event.target.checked }))}
          />
        </div>
      </div>

      {/* ---- Roles ---- */}
      <div className="mt-4 border-t border-ink-100 pt-4">
        <div className="flex items-baseline justify-between gap-2">
          <h3 className="text-sm font-semibold text-ink-800">Roles</h3>
          <p className="text-xs text-ink-400">Roles are how capability is normally granted.</p>
        </div>

        {!canAssignRoles && (
          <div className="mt-2">
            <Alert tone="info">
              You can see this person’s roles, but changing them needs the “users.assign-roles”
              permission.
            </Alert>
          </div>
        )}

        <div className="mt-2 grid gap-0.5 sm:grid-cols-2 lg:grid-cols-3">
          {allRoles.data?.map((role) => {
            /*
              Only a System user may grant the System role — it bypasses every permission check,
              so without this an admin holding users.assign-roles could promote themselves to
              unrestricted access. The server refuses it too; this stops the attempt earlier and
              explains why.
            */
            const systemLocked = role.name === 'System' && !actor?.isSystem

            return (
              <Checkbox
                key={role.id}
                label={role.name}
                hint={
                  systemLocked
                    ? 'Only a System user can grant this'
                    : role.description ??
                      (role.name === 'System'
                        ? 'Unrestricted — bypasses every permission check'
                        : `${role.permissions.length} permissions`)
                }
                checked={roles.includes(role.name)}
                disabled={!canAssignRoles || systemLocked}
                onChange={(event) =>
                  setRoles((current) =>
                    event.target.checked
                      ? [...current, role.name]
                      : current.filter((name) => name !== role.name),
                  )
                }
              />
            )
          })}
        </div>
      </div>

      <div className="mt-4 flex gap-2">
        <Button loading={saveProfile.isPending} onClick={() => saveProfile.mutate()}>
          Save user
        </Button>
        <Button variant="ghost" onClick={onClose}>
          Cancel
        </Button>
      </div>

      {/* ---- Per-user overrides ---- */}
      {canManagePermissions && (
        <div className="mt-5 border-t border-ink-100 pt-4">
          <button
            type="button"
            onClick={() => setShowOverrides((open) => !open)}
            aria-expanded={showOverrides}
            className="flex w-full items-center justify-between gap-2 text-left"
          >
            <span>
              <span className="text-sm font-semibold text-ink-800">Permission overrides</span>
              <span className="block text-xs text-ink-400">
                For the one person who needs an exception. Prefer a role where you can.
              </span>
            </span>
            <span className="flex items-center gap-2 text-xs text-ink-500">
              {overrideCount > 0 && <Badge tone="warning">{overrideCount} set</Badge>}
              {showOverrides ? 'Hide' : 'Show'}
            </span>
          </button>

          {showOverrides && (
            <>
              {savePermissions.isError && (
                <div className="mt-3">
                  <Alert tone="error">{(savePermissions.error as Error).message}</Alert>
                </div>
              )}

              {loaded.roles.includes('System') && (
                <div className="mt-3">
                  <Alert tone="info">
                    This person holds the System role, which bypasses permission evaluation
                    entirely — overrides set here will have no effect while they keep it.
                  </Alert>
                </div>
              )}

              <div className="mt-3 space-y-4">
                {catalogue.data?.map((group) => (
                  <div key={group.module}>
                    <p className="mb-1.5 border-b border-ink-100 pb-1.5 text-xs font-semibold uppercase tracking-wide text-ink-500">
                      {group.module}
                    </p>

                    <ul className="space-y-0.5">
                      {group.permissions.map((permission) => {
                        const state = overrides[permission.code] ?? 'inherit'
                        const inherited = fromRoles.has(permission.code)

                        return (
                          <li
                            key={permission.id}
                            className="flex flex-wrap items-center justify-between gap-2 rounded-lg px-2 py-1.5 hover:bg-ink-50"
                          >
                            <span className="min-w-0">
                              <span className="block text-sm text-ink-700">{permission.displayName}</span>
                              <span className="block font-mono text-[11px] text-ink-400">
                                {permission.code}
                              </span>
                            </span>

                            <span className="flex items-center gap-2">
                              <span
                                className={`text-[11px] ${inherited ? 'text-success-700' : 'text-ink-300'}`}
                              >
                                {inherited ? 'from role' : 'not in roles'}
                              </span>

                              <Select
                                value={state}
                                aria-label={`Override for ${permission.code}`}
                                onChange={(event) =>
                                  setOverrides((current) => ({
                                    ...current,
                                    [permission.code]: event.target.value as OverrideState,
                                  }))
                                }
                                className={`h-8 w-28 text-xs ${
                                  state === 'grant'
                                    ? 'border-success-300'
                                    : state === 'deny'
                                      ? 'border-chilli-300'
                                      : ''
                                }`}
                              >
                                <option value="inherit">Inherit</option>
                                <option value="grant">Grant</option>
                                <option value="deny">Deny</option>
                              </Select>
                            </span>
                          </li>
                        )
                      })}
                    </ul>
                  </div>
                ))}
              </div>

              <div className="mt-4 flex justify-end gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    setOverrides(
                      Object.fromEntries(
                        loaded.overrides.map((override) => [
                          override.permissionCode,
                          override.isGranted ? 'grant' : 'deny',
                        ]),
                      ),
                    )
                  }
                >
                  Reset
                </Button>
                <Button size="sm" loading={savePermissions.isPending} onClick={() => savePermissions.mutate()}>
                  Save overrides
                </Button>
              </div>
            </>
          )}
        </div>
      )}
    </section>
  )
}

const BLANK_NEW_USER = {
  email: '',
  password: '',
  firstName: '',
  lastName: '',
  phone: '',
  isActive: true,
}

/** Creates an account. Roles are picked here too, so a staff member can be set up in one step. */
export function NewUserForm({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const { can, user: actor } = useAuth()

  const [form, setForm] = useState(BLANK_NEW_USER)
  const [roles, setRoles] = useState<string[]>(['Customer'])

  const allRoles = useQuery({
    queryKey: ['admin', 'roles'],
    queryFn: () => api.get<Role[]>('/admin/roles'),
  })

  const create = useMutation({
    mutationFn: () =>
      api.post<{ id: string }>('/admin/users', {
        email: form.email.trim().toLowerCase(),
        password: form.password,
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        phone: form.phone.trim() || null,
        roles,
        isActive: form.isActive,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'users'] })
      await queryClient.invalidateQueries({ queryKey: ['admin', 'roles'] })
      onClose()
    },
  })

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        create.mutate()
      }}
      className="card-surface animate-fade-rise mb-5 p-5"
    >
      <h2 className="font-display text-base font-bold text-ink-900">New user</h2>

      {create.isError && (
        <div className="mt-3">
          <Alert tone="error">{(create.error as Error).message}</Alert>
        </div>
      )}

      <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Field label="Email" htmlFor="new-email" required>
          <Input
            id="new-email"
            type="email"
            value={form.email}
            onChange={(event) => setForm((f) => ({ ...f, email: event.target.value }))}
            required
            autoFocus
          />
        </Field>

        <Field
          label="Password"
          htmlFor="new-password"
          required
          hint="They can change it after signing in"
        >
          <Input
            id="new-password"
            type="password"
            value={form.password}
            onChange={(event) => setForm((f) => ({ ...f, password: event.target.value }))}
            required
            minLength={8}
            autoComplete="new-password"
          />
        </Field>

        <Field label="Phone" htmlFor="new-phone">
          <Input
            id="new-phone"
            value={form.phone}
            onChange={(event) => setForm((f) => ({ ...f, phone: event.target.value }))}
          />
        </Field>

        <Field label="First name" htmlFor="new-first" required>
          <Input
            id="new-first"
            value={form.firstName}
            onChange={(event) => setForm((f) => ({ ...f, firstName: event.target.value }))}
            required
          />
        </Field>

        <Field label="Last name" htmlFor="new-last" required>
          <Input
            id="new-last"
            value={form.lastName}
            onChange={(event) => setForm((f) => ({ ...f, lastName: event.target.value }))}
            required
          />
        </Field>

        <div className="pt-6">
          <Checkbox
            label="Active"
            checked={form.isActive}
            onChange={(event) => setForm((f) => ({ ...f, isActive: event.target.checked }))}
          />
        </div>
      </div>

      <div>
        <h3 className="text-sm font-semibold text-ink-800">Roles</h3>

        {!can('users.assign-roles') && (
          <div className="mt-2">
            <Alert tone="info">
              Without “users.assign-roles” you can only create a Customer account.
            </Alert>
          </div>
        )}

        <div className="mt-2 grid gap-0.5 sm:grid-cols-2 lg:grid-cols-3">
          {allRoles.data?.map((role) => {
            const systemLocked = role.name === 'System' && !actor?.isSystem
            const roleLocked = role.name !== 'Customer' && !can('users.assign-roles')

            return (
              <Checkbox
                key={role.id}
                label={role.name}
                disabled={systemLocked || roleLocked}
                hint={systemLocked ? 'Only a System user can grant this' : undefined}
                checked={roles.includes(role.name)}
                onChange={(event) =>
                  setRoles((current) =>
                    event.target.checked
                      ? [...current, role.name]
                      : current.filter((name) => name !== role.name),
                  )
                }
              />
            )
          })}
        </div>
      </div>

      <div className="mt-4 flex gap-2">
        <Button type="submit" loading={create.isPending}>
          Create user
        </Button>
        <Button type="button" variant="ghost" onClick={onClose}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
