import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { Alert, Badge, Button, Input, Select } from '../../ui/primitives'
import { PageHeader } from '../../features/admin/AdminTable'
import { useAuth } from '../../app/providers/AuthProvider'

interface Setting {
  key: string
  value?: string
  group: string
  dataType: string
  displayName?: string
  description?: string
  isPublic: boolean
}

export default function AdminSettingsPage() {
  const queryClient = useQueryClient()
  const { can } = useAuth()
  const canManage = can('settings.manage')

  // Only the settings the operator has actually edited are sent, so an untouched field can never
  // be overwritten by a stale value this page happened to be holding.
  const [dirty, setDirty] = useState<Record<string, string>>({})
  const [saved, setSaved] = useState(false)

  const { data: settings, isLoading } = useQuery({
    queryKey: ['admin', 'settings'],
    queryFn: () => api.get<Setting[]>('/admin/settings'),
  })

  const save = useMutation({
    mutationFn: () => api.put<void>('/admin/settings', dirty),
    onSuccess: async () => {
      setDirty({})
      setSaved(true)
      await queryClient.invalidateQueries({ queryKey: ['admin', 'settings'] })
      // The storefront shell caches the public subset, so it must be refetched to pick this up.
      await queryClient.invalidateQueries({ queryKey: ['storefront', 'bootstrap'] })
    },
  })

  // Clear the confirmation once the operator starts editing again.
  useEffect(() => {
    if (Object.keys(dirty).length > 0) {
      setSaved(false)
    }
  }, [dirty])

  if (isLoading || !settings) {
    return (
      <div aria-busy="true">
        <PageHeader title="Settings" />
        <div className="space-y-3">
          {Array.from({ length: 4 }, (_, index) => (
            <div key={index} className="skeleton h-40 w-full" />
          ))}
        </div>
      </div>
    )
  }

  const groups = settings.reduce<Record<string, Setting[]>>((accumulator, setting) => {
    ;(accumulator[setting.group] ??= []).push(setting)
    return accumulator
  }, {})

  const dirtyCount = Object.keys(dirty).length

  return (
    <div>
      <PageHeader
        title="Settings"
        description="Store details, checkout rules and catalogue behaviour."
        action={
          canManage ? (
            <Button loading={save.isPending} disabled={dirtyCount === 0} onClick={() => save.mutate()}>
              {dirtyCount === 0 ? 'No changes' : `Save ${dirtyCount} change${dirtyCount === 1 ? '' : 's'}`}
            </Button>
          ) : undefined
        }
      />

      {!canManage && (
        <div className="mb-4">
          <Alert tone="info">You can view these settings but not change them.</Alert>
        </div>
      )}

      {saved && (
        <div className="mb-4">
          <Alert tone="success">Settings saved. The storefront will pick them up immediately.</Alert>
        </div>
      )}

      {save.isError && (
        <div className="mb-4">
          <Alert tone="error">{(save.error as Error).message}</Alert>
        </div>
      )}

      <div className="space-y-4">
        {Object.entries(groups).map(([group, items]) => (
          <section key={group} className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">{group}</h2>

            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              {items.map((setting) => {
                const current = dirty[setting.key] ?? setting.value ?? ''
                const isChanged = setting.key in dirty

                return (
                  <div key={setting.key}>
                    <label
                      htmlFor={setting.key}
                      className="flex items-center gap-2 text-sm font-medium text-ink-700"
                    >
                      {setting.displayName ?? setting.key}
                      {isChanged && <Badge tone="warning">Changed</Badge>}
                      {/* Public settings reach anonymous visitors, so it is worth being explicit
                          about which ones do. */}
                      {setting.isPublic && <Badge tone="neutral">Public</Badge>}
                    </label>

                    {setting.dataType === 'boolean' ? (
                      <Select
                        id={setting.key}
                        value={current}
                        disabled={!canManage}
                        onChange={(event) =>
                          setDirty((d) => ({ ...d, [setting.key]: event.target.value }))
                        }
                        className="mt-1.5"
                      >
                        <option value="true">Yes</option>
                        <option value="false">No</option>
                      </Select>
                    ) : (
                      <Input
                        id={setting.key}
                        type={setting.dataType === 'number' ? 'number' : 'text'}
                        inputMode={setting.dataType === 'number' ? 'numeric' : undefined}
                        value={current}
                        disabled={!canManage}
                        onChange={(event) =>
                          setDirty((d) => ({ ...d, [setting.key]: event.target.value }))
                        }
                        className="mt-1.5"
                      />
                    )}

                    <p className="mt-1 font-mono text-[10px] text-ink-300">{setting.key}</p>
                    {setting.description && (
                      <p className="mt-0.5 text-xs text-ink-400">{setting.description}</p>
                    )}
                  </div>
                )
              })}
            </div>
          </section>
        ))}
      </div>
    </div>
  )
}
