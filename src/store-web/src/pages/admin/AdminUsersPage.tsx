import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Alert, Badge, Button, Input } from '../../ui/primitives'
import { FilterBar, PageHeader, Pager, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { useAuth } from '../../app/providers/AuthProvider'
import { formatDate } from '../../lib/format'
import type { Paged } from '../../lib/types'

interface UserRow {
  id: string
  email: string
  fullName: string
  phone?: string
  avatarUrl?: string
  isActive: boolean
  emailConfirmed: boolean
  roles: string[]
  createdAt: string
  lastLoginAt?: string
}

interface Role {
  id: string
  name: string
  description?: string
  isSystemRole: boolean
  userCount: number
  permissions: string[]
}

interface PermissionModule {
  module: string
  permissions: { id: string; code: string; module: string; displayName: string; description?: string }[]
}

export default function AdminUsersPage() {
  const { user, can } = useAuth()
  const [tab, setTab] = useState<'users' | 'roles'>('users')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)

  const users = useQuery({
    queryKey: ['admin', 'users', { search, page }],
    queryFn: () => api.get<Paged<UserRow>>(`/admin/users${qs({ search, page, pageSize: 20 })}`),
    enabled: tab === 'users' && can('users.view'),
    placeholderData: keepPreviousData,
  })

  const roles = useQuery({
    queryKey: ['admin', 'roles'],
    queryFn: () => api.get<Role[]>('/admin/roles'),
    enabled: tab === 'roles' && can('roles.view'),
  })

  const permissions = useQuery({
    queryKey: ['admin', 'permissions'],
    queryFn: () => api.get<PermissionModule[]>('/admin/permissions'),
    enabled: tab === 'roles',
  })

  return (
    <div>
      <PageHeader
        title="Users & roles"
        description="Admin capabilities come entirely from permissions. Only a System user can change them."
      />

      <div className="mb-5 flex gap-1 border-b border-ink-100">
        {(['users', 'roles'] as const).map((value) => (
          <button
            key={value}
            type="button"
            onClick={() => setTab(value)}
            className={`-mb-px border-b-2 px-4 py-2 text-sm font-medium capitalize transition-colors ${
              tab === value
                ? 'border-saffron-500 text-saffron-700'
                : 'border-transparent text-ink-500 hover:text-ink-700'
            }`}
          >
            {value}
          </button>
        ))}
      </div>

      {tab === 'users' && (
        <>
          <FilterBar>
            <form
              onSubmit={(event) => {
                event.preventDefault()
                setPage(1)
              }}
              className="flex gap-2"
            >
              <Input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Name, email or phone…"
                className="h-9 w-72"
                aria-label="Search users"
              />
              <Button type="submit" variant="outline" size="sm">
                Search
              </Button>
            </form>
          </FilterBar>

          {users.isLoading ? (
            <TableSkeleton columns={5} />
          ) : (
            <>
              <TableShell>
                <thead>
                  <tr>
                    <Th>User</Th>
                    <Th>Roles</Th>
                    <Th>Joined</Th>
                    <Th>Last signed in</Th>
                    <Th>Status</Th>
                  </tr>
                </thead>
                <tbody>
                  {users.data?.items.length === 0 && <TableEmpty colSpan={5} message="No users match that search." />}

                  {users.data?.items.map((row) => (
                    <tr key={row.id} className="transition-colors hover:bg-ink-50/60">
                      <Td>
                        <div className="flex items-center gap-3">
                          <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-ink-100 text-xs font-bold text-ink-600">
                            {row.fullName.trim()[0]?.toUpperCase() ?? '?'}
                          </span>
                          <div className="min-w-0">
                            <span className="block truncate font-medium text-ink-800">
                              {row.fullName}
                              {row.id === user?.id && (
                                <span className="ml-1.5 text-[10px] font-normal text-ink-400">(you)</span>
                              )}
                            </span>
                            <span className="block truncate text-[11px] text-ink-400">{row.email}</span>
                          </div>
                        </div>
                      </Td>

                      <Td>
                        <div className="flex flex-wrap gap-1">
                          {row.roles.map((role) => (
                            <Badge key={role} tone={role === 'System' ? 'new' : 'neutral'}>
                              {role}
                            </Badge>
                          ))}
                        </div>
                      </Td>

                      <Td className="whitespace-nowrap text-xs text-ink-500">{formatDate(row.createdAt)}</Td>

                      <Td className="whitespace-nowrap text-xs text-ink-500">
                        {row.lastLoginAt ? formatDate(row.lastLoginAt) : 'Never'}
                      </Td>

                      <Td>
                        <Badge tone={row.isActive ? 'success' : 'neutral'}>
                          {row.isActive ? 'Active' : 'Deactivated'}
                        </Badge>
                      </Td>
                    </tr>
                  ))}
                </tbody>
              </TableShell>

              {users.data && (
                <Pager
                  page={users.data.page}
                  totalPages={users.data.totalPages}
                  totalCount={users.data.totalCount}
                  onChange={setPage}
                />
              )}
            </>
          )}
        </>
      )}

      {tab === 'roles' && (
        <div className="space-y-4">
          {!user?.isSystem && (
            <Alert tone="info">
              Only a System user can change what a role can do. You can see the current setup here.
            </Alert>
          )}

          {roles.isLoading ? (
            <div className="space-y-3" aria-busy="true">
              {Array.from({ length: 3 }, (_, index) => (
                <div key={index} className="skeleton h-32 w-full" />
              ))}
            </div>
          ) : (
            roles.data?.map((role) => (
              <section key={role.id} className="card-surface p-5">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="flex items-center gap-2">
                      <h2 className="font-display text-base font-bold text-ink-900">{role.name}</h2>
                      {role.isSystemRole && <Badge tone="neutral">Built in</Badge>}
                    </div>
                    {role.description && <p className="mt-1 text-sm text-ink-500">{role.description}</p>}
                  </div>

                  <span className="text-xs text-ink-400">
                    {role.userCount} member{role.userCount === 1 ? '' : 's'}
                  </span>
                </div>

                {/*
                  The System role holds no permission rows on purpose — it bypasses permission
                  evaluation entirely, so listing grants for it would be misleading.
                */}
                {role.name === 'System' ? (
                  <p className="mt-4 rounded-lg bg-saffron-50 p-3 text-xs leading-relaxed text-saffron-800">
                    Unrestricted by definition. The System role bypasses every permission check, so
                    it holds no individual grants and none can be added.
                  </p>
                ) : role.permissions.length === 0 ? (
                  <p className="mt-4 text-sm text-ink-400">No permissions granted.</p>
                ) : (
                  <div className="mt-4">
                    <p className="text-xs font-semibold uppercase tracking-wide text-ink-400">
                      {role.permissions.length} permissions
                    </p>
                    <div className="mt-2 flex flex-wrap gap-1">
                      {role.permissions.map((code) => (
                        <span
                          key={code}
                          className="rounded-md bg-ink-50 px-2 py-0.5 font-mono text-[10px] text-ink-600"
                        >
                          {code}
                        </span>
                      ))}
                    </div>
                  </div>
                )}
              </section>
            ))
          )}

          {permissions.data && (
            <section className="card-surface p-5">
              <h2 className="font-display text-base font-bold text-ink-900">Permission catalogue</h2>
              <p className="mt-1 text-sm text-ink-500">
                Every capability the system defines, grouped by area.
              </p>

              <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
                {permissions.data.map((group) => (
                  <div key={group.module}>
                    <p className="text-xs font-semibold uppercase tracking-wide text-ink-500">
                      {group.module}
                    </p>
                    <ul className="mt-1.5 space-y-0.5">
                      {group.permissions.map((permission) => (
                        <li key={permission.id} className="font-mono text-[11px] text-ink-500">
                          {permission.code}
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            </section>
          )}
        </div>
      )}
    </div>
  )
}
