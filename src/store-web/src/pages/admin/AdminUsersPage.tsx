import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Badge, Button, Input } from '../../ui/primitives'
import { FilterBar, PageHeader, Pager, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { useAuth } from '../../app/providers/AuthProvider'
import { RolesPanel } from '../../features/admin/RolesPanel'
import { NewUserForm, UserEditor } from '../../features/admin/UserEditor'
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

export default function AdminUsersPage() {
  const { user, can } = useAuth()
  const [tab, setTab] = useState<'users' | 'roles'>('users')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  const users = useQuery({
    queryKey: ['admin', 'users', { search, page }],
    queryFn: () => api.get<Paged<UserRow>>(`/admin/users${qs({ search, page, pageSize: 20 })}`),
    enabled: tab === 'users' && can('users.view'),
    placeholderData: keepPreviousData,
  })


  return (
    <div>
      <PageHeader
        title="Users & roles"
        description="Every admin capability is a permission, and a role is a set of them. Changing a role needs the roles.assign-permissions permission."
        action={
          tab === 'users' && can('users.create') && !creating ? (
            <Button
              onClick={() => {
                setEditingId(null)
                setCreating(true)
              }}
            >
              New user
            </Button>
          ) : undefined
        }
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
          {creating && <NewUserForm onClose={() => setCreating(false)} />}

          {editingId && !creating && (
            <UserEditor key={editingId} userId={editingId} onClose={() => setEditingId(null)} />
          )}

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
                    <Th align="right"> </Th>
                  </tr>
                </thead>
                <tbody>
                  {users.data?.items.length === 0 && <TableEmpty colSpan={6} message="No users match that search." />}

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

                      <Td align="right">
                        {can('users.update') && (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => {
                              setCreating(false)
                              setEditingId(editingId === row.id ? null : row.id)
                            }}
                          >
                            {editingId === row.id ? 'Close' : 'Manage'}
                          </Button>
                        )}
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

      {tab === 'roles' && <RolesPanel />}

    </div>
  )
}
