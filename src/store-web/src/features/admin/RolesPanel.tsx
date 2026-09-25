import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { Alert, Badge, Button, Checkbox, Field, Input, Textarea } from '../../ui/primitives'
import { useAuth } from '../../app/providers/AuthProvider'

export interface Role {
  id: string
  name: string
  description?: string
  isSystemRole: boolean
  userCount: number
  permissions: string[]
}

export interface PermissionModule {
  module: string
  permissions: { id: string; code: string; module: string; displayName: string; description?: string }[]
}

/**
 * Role administration: the list, the details, and the permission matrix.
 *
 * Every admin capability in the application is a permission code, and a role is nothing but a set
 * of them — so this screen is where an administrator's reach is actually decided. It is therefore
 * gated hard: the buttons are hidden without the matching permission, and every one of them hits
 * an endpoint that checks the same permission again.
 */
export function RolesPanel() {
  const queryClient = useQueryClient()
  const { can } = useAuth()

  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [draft, setDraft] = useState<string[]>([])
  const [editing, setEditing] = useState<Role | 'new' | null>(null)
  const [form, setForm] = useState({ name: '', description: '' })

  const roles = useQuery({
    queryKey: ['admin', 'roles'],
    queryFn: () => api.get<Role[]>('/admin/roles'),
  })

  const permissions = useQuery({
    queryKey: ['admin', 'permissions'],
    queryFn: () => api.get<PermissionModule[]>('/admin/permissions'),
  })

  const selected = useMemo(
    () => roles.data?.find((role) => role.id === selectedId) ?? roles.data?.[0] ?? null,
    [roles.data, selectedId],
  )

  /*
    The System role bypasses permission evaluation entirely rather than holding every grant, so
    its permission list is empty by design. Rendering an empty matrix for it would read as "this
    role can do nothing" — the exact opposite of the truth.
  */
  const isUnrestricted = selected?.name === 'System'
  const editable = selected !== null && !isUnrestricted && can('roles.assign-permissions')

  // Resync the draft when the selection changes, or when a save returns a new server state.
  useEffect(() => {
    if (selected) setDraft(selected.permissions)
  }, [selected?.id, selected?.permissions])

  const dirty =
    selected !== null &&
    (draft.length !== selected.permissions.length ||
      draft.some((code) => !selected.permissions.includes(code)))

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin', 'roles'] })

  const savePermissions = useMutation({
    mutationFn: (roleId: string) => api.put<void>(`/admin/roles/${roleId}/permissions`, { permissions: draft }),
    onSuccess: invalidate,
  })

  const saveRole = useMutation({
    mutationFn: () => {
      const payload = { name: form.name.trim(), description: form.description.trim() || null }

      return editing === 'new'
        ? api.post<Role>('/admin/roles', { ...payload, permissions: [] })
        : api.put<Role>(`/admin/roles/${(editing as Role).id}`, payload)
    },
    onSuccess: async (saved) => {
      setEditing(null)
      await invalidate()
      if (saved?.id) setSelectedId(saved.id)
    },
  })

  const removeRole = useMutation({
    mutationFn: (roleId: string) => api.del<void>(`/admin/roles/${roleId}`),
    onSuccess: async () => {
      setSelectedId(null)
      await invalidate()
    },
  })

  function toggle(code: string) {
    setDraft((current) =>
      current.includes(code) ? current.filter((existing) => existing !== code) : [...current, code],
    )
  }

  function toggleModule(codes: string[], allOn: boolean) {
    setDraft((current) =>
      allOn
        ? current.filter((code) => !codes.includes(code))
        : [...new Set([...current, ...codes])],
    )
  }

  if (roles.isLoading) {
    return (
      <div className="grid gap-4 lg:grid-cols-[18rem_1fr]" aria-busy="true">
        <div className="space-y-2">
          {Array.from({ length: 3 }, (_, index) => (
            <div key={index} className="skeleton h-20 w-full" />
          ))}
        </div>
        <div className="skeleton h-96 w-full" />
      </div>
    )
  }

  if (roles.isError) {
    return <Alert tone="error">{(roles.error as Error).message}</Alert>
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-ink-500">
          Define a role once, then assign it to people on the Users tab.
        </p>
        {can('roles.create') && editing === null && (
          <Button
            size="sm"
            onClick={() => {
              setForm({ name: '', description: '' })
              setEditing('new')
            }}
          >
            New role
          </Button>
        )}
      </div>

      {editing !== null && (
        <form
          onSubmit={(event) => {
            event.preventDefault()
            saveRole.mutate()
          }}
          className="card-surface animate-fade-rise p-5"
        >
          <h3 className="font-display text-base font-bold text-ink-900">
            {editing === 'new' ? 'New role' : `Edit ${(editing as Role).name}`}
          </h3>

          {saveRole.isError && (
            <div className="mt-3">
              <Alert tone="error">{(saveRole.error as Error).message}</Alert>
            </div>
          )}

          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            <Field label="Name" htmlFor="role-name" required hint="e.g. Warehouse, Buyer, Cashier">
              <Input
                id="role-name"
                value={form.name}
                onChange={(event) => setForm((f) => ({ ...f, name: event.target.value }))}
                required
                autoFocus
              />
            </Field>

            <Field label="Description" htmlFor="role-description" hint="What this role is for">
              <Textarea
                id="role-description"
                rows={2}
                value={form.description}
                onChange={(event) => setForm((f) => ({ ...f, description: event.target.value }))}
              />
            </Field>
          </div>

          <div className="flex gap-2">
            <Button type="submit" loading={saveRole.isPending}>
              {editing === 'new' ? 'Create role' : 'Save role'}
            </Button>
            <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
          </div>

          {editing === 'new' && (
            <p className="mt-2 text-xs text-ink-400">
              The role is created with no permissions. Tick what it may do once it appears in the
              list.
            </p>
          )}
        </form>
      )}

      <div className="grid gap-4 lg:grid-cols-[18rem_1fr]">
        {/* ---- Role list ---- */}
        <ul className="space-y-2">
          {roles.data?.map((role) => {
            const active = role.id === selected?.id

            return (
              <li key={role.id}>
                <button
                  type="button"
                  onClick={() => setSelectedId(role.id)}
                  aria-current={active ? 'true' : undefined}
                  className={`w-full rounded-xl border p-3.5 text-left transition-colors ${
                    active
                      ? 'border-saffron-300 bg-saffron-50'
                      : 'border-ink-100 bg-paper-raised hover:bg-ink-50'
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <span className="font-semibold text-ink-900">{role.name}</span>
                    {role.isSystemRole && <Badge tone="neutral">Built in</Badge>}
                  </div>
                  <p className="mt-1 text-xs text-ink-400">
                    {role.userCount} member{role.userCount === 1 ? '' : 's'} ·{' '}
                    {role.name === 'System' ? 'unrestricted' : `${role.permissions.length} permissions`}
                  </p>
                </button>
              </li>
            )
          })}
        </ul>

        {/* ---- Permission matrix ---- */}
        {selected && (
          <section className="card-surface p-5">
            <header className="flex flex-wrap items-start justify-between gap-3 border-b border-ink-100 pb-4">
              <div>
                <h3 className="font-display text-base font-bold text-ink-900">{selected.name}</h3>
                {selected.description && (
                  <p className="mt-0.5 text-sm text-ink-500">{selected.description}</p>
                )}
              </div>

              {!selected.isSystemRole && (
                <div className="flex gap-1">
                  {can('roles.update') && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => {
                        setForm({ name: selected.name, description: selected.description ?? '' })
                        setEditing(selected)
                      }}
                    >
                      Rename
                    </Button>
                  )}
                  {can('roles.delete') && (
                    <Button
                      variant="ghost"
                      size="sm"
                      className="text-chilli-600"
                      loading={removeRole.isPending}
                      onClick={() => {
                        if (
                          window.confirm(
                            `Delete "${selected.name}"? ${selected.userCount} member(s) will lose it. They keep their other roles.`,
                          )
                        ) {
                          removeRole.mutate(selected.id)
                        }
                      }}
                    >
                      Delete
                    </Button>
                  )}
                </div>
              )}
            </header>

            {removeRole.isError && (
              <div className="mt-4">
                <Alert tone="error">{(removeRole.error as Error).message}</Alert>
              </div>
            )}

            {isUnrestricted ? (
              <Alert tone="info" title="Unrestricted by definition">
                The System role bypasses every permission check, so it holds no individual grants
                and none can be added. Give someone this role only when they should be able to do
                anything at all.
              </Alert>
            ) : (
              <>
                {!can('roles.assign-permissions') && (
                  <div className="mt-4">
                    <Alert tone="info">
                      You can see what this role allows, but changing it needs the
                      “roles.assign-permissions” permission.
                    </Alert>
                  </div>
                )}

                {savePermissions.isError && (
                  <div className="mt-4">
                    <Alert tone="error">{(savePermissions.error as Error).message}</Alert>
                  </div>
                )}

                <div className="mt-4 space-y-5">
                  {permissions.data?.map((group) => {
                    const codes = group.permissions.map((permission) => permission.code)
                    const allOn = codes.every((code) => draft.includes(code))
                    const someOn = !allOn && codes.some((code) => draft.includes(code))

                    return (
                      <div key={group.module}>
                        <div className="mb-1.5 flex items-center justify-between border-b border-ink-100 pb-1.5">
                          <p className="text-xs font-semibold uppercase tracking-wide text-ink-500">
                            {group.module}
                            {someOn && <span className="ml-2 font-normal text-ink-300">partial</span>}
                          </p>
                          {editable && (
                            <button
                              type="button"
                              onClick={() => toggleModule(codes, allOn)}
                              className="text-xs font-medium text-saffron-600 transition-colors hover:text-saffron-700"
                            >
                              {allOn ? 'Clear' : 'Select all'}
                            </button>
                          )}
                        </div>

                        <div className="grid gap-0.5 sm:grid-cols-2">
                          {group.permissions.map((permission) => (
                            <Checkbox
                              key={permission.id}
                              label={permission.displayName}
                              hint={permission.code}
                              checked={draft.includes(permission.code)}
                              disabled={!editable}
                              onChange={() => toggle(permission.code)}
                            />
                          ))}
                        </div>
                      </div>
                    )
                  })}
                </div>

                {editable && (
                  <div className="mt-5 flex items-center justify-end gap-3 border-t border-ink-100 pt-4">
                    {dirty && <span className="text-xs text-ink-400">Unsaved changes</span>}
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={!dirty}
                      onClick={() => setDraft(selected.permissions)}
                    >
                      Reset
                    </Button>
                    <Button
                      size="sm"
                      disabled={!dirty}
                      loading={savePermissions.isPending}
                      onClick={() => savePermissions.mutate(selected.id)}
                    >
                      Save permissions
                    </Button>
                  </div>
                )}
              </>
            )}
          </section>
        )}
      </div>
    </div>
  )
}
