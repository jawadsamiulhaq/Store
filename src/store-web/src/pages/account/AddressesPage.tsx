import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { Alert, Badge, Button, EmptyState, Field, Input, Select } from '../../ui/primitives'
import type { Address } from '../../lib/types'

const REGIONS = ['Kowloon', 'Hong Kong Island', 'New Territories'] as const

const BLANK = {
  label: '',
  fullName: '',
  phone: '',
  line1: '',
  line2: '',
  district: '',
  region: 'Kowloon' as string,
  isDefaultShipping: false,
}

export default function AddressesPage() {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<string | 'new' | null>(null)
  const [form, setForm] = useState(BLANK)

  const { data: addresses, isLoading } = useQuery({
    queryKey: ['addresses'],
    queryFn: () => api.get<Address[]>('/addresses'),
  })

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['addresses'] })

  const save = useMutation({
    mutationFn: () => {
      const payload = {
        label: form.label.trim() || null,
        fullName: form.fullName.trim(),
        phone: form.phone.trim(),
        line1: form.line1.trim(),
        line2: form.line2.trim() || null,
        district: form.district.trim() || null,
        city: 'Hong Kong',
        region: form.region,
        postalCode: null,
        countryCode: 'HK',
        isDefaultShipping: form.isDefaultShipping,
        isDefaultBilling: form.isDefaultShipping,
      }

      return editing === 'new'
        ? api.post<Address>('/addresses', payload)
        : api.put<Address>(`/addresses/${editing}`, payload)
    },
    onSuccess: async () => {
      setEditing(null)
      setForm(BLANK)
      await invalidate()
    },
  })

  const remove = useMutation({
    mutationFn: (addressId: string) => api.del<void>(`/addresses/${addressId}`),
    onSuccess: invalidate,
  })

  const setDefault = useMutation({
    mutationFn: (addressId: string) => api.post<void>(`/addresses/${addressId}/default`),
    onSuccess: invalidate,
  })

  function startEdit(address: Address) {
    setForm({
      label: address.label ?? '',
      fullName: address.fullName,
      phone: address.phone,
      line1: address.line1,
      line2: address.line2 ?? '',
      district: address.district ?? '',
      region: address.region ?? 'Kowloon',
      isDefaultShipping: address.isDefaultShipping,
    })
    setEditing(address.id)
  }

  if (isLoading) {
    return (
      <div className="space-y-3" aria-busy="true">
        {Array.from({ length: 2 }, (_, index) => (
          <div key={index} className="skeleton h-36 w-full" />
        ))}
      </div>
    )
  }

  return (
    <div className="max-w-2xl space-y-4">
      {editing === null && (
        <div className="flex justify-end">
          <Button
            onClick={() => {
              setForm(BLANK)
              setEditing('new')
            }}
          >
            Add an address
          </Button>
        </div>
      )}

      {editing !== null && (
        <form
          onSubmit={(event) => {
            event.preventDefault()
            save.mutate()
          }}
          className="card-surface animate-fade-rise p-5"
        >
          <h2 className="font-display text-base font-bold text-ink-900">
            {editing === 'new' ? 'New address' : 'Edit address'}
          </h2>

          {save.isError && (
            <div className="mt-4">
              <Alert tone="error">{(save.error as Error).message}</Alert>
            </div>
          )}

          <div className="mt-4 space-y-1">
            <Field label="Label" htmlFor="label" hint="e.g. Home, Office">
              <Input
                id="label"
                value={form.label}
                onChange={(event) => setForm((f) => ({ ...f, label: event.target.value }))}
              />
            </Field>

            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Recipient name" htmlFor="fullName" required>
                <Input
                  id="fullName"
                  autoComplete="name"
                  required
                  value={form.fullName}
                  onChange={(event) => setForm((f) => ({ ...f, fullName: event.target.value }))}
                />
              </Field>

              <Field label="Phone" htmlFor="phone" required>
                <Input
                  id="phone"
                  type="tel"
                  inputMode="tel"
                  autoComplete="tel"
                  required
                  value={form.phone}
                  onChange={(event) => setForm((f) => ({ ...f, phone: event.target.value }))}
                />
              </Field>
            </div>

            <Field label="Flat, floor and building" htmlFor="line1" required>
              <Input
                id="line1"
                autoComplete="address-line1"
                required
                value={form.line1}
                onChange={(event) => setForm((f) => ({ ...f, line1: event.target.value }))}
              />
            </Field>

            <Field label="Estate or street" htmlFor="line2">
              <Input
                id="line2"
                autoComplete="address-line2"
                value={form.line2}
                onChange={(event) => setForm((f) => ({ ...f, line2: event.target.value }))}
              />
            </Field>

            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="District" htmlFor="district" hint="e.g. Wong Tai Sin">
                <Input
                  id="district"
                  value={form.district}
                  onChange={(event) => setForm((f) => ({ ...f, district: event.target.value }))}
                />
              </Field>

              <Field label="Region" htmlFor="region" required>
                <Select
                  id="region"
                  value={form.region}
                  onChange={(event) => setForm((f) => ({ ...f, region: event.target.value }))}
                >
                  {REGIONS.map((region) => (
                    <option key={region} value={region}>
                      {region}
                    </option>
                  ))}
                </Select>
              </Field>
            </div>

            <label className="flex items-center gap-2 text-sm text-ink-600">
              <input
                type="checkbox"
                checked={form.isDefaultShipping}
                onChange={(event) => setForm((f) => ({ ...f, isDefaultShipping: event.target.checked }))}
                className="h-4 w-4 rounded border-ink-300 text-saffron-500"
              />
              Use as my default delivery address
            </label>
          </div>

          <div className="mt-5 flex gap-2">
            <Button type="submit" loading={save.isPending}>
              Save address
            </Button>
            <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
          </div>
        </form>
      )}

      {!addresses || addresses.length === 0 ? (
        editing === null && (
          <EmptyState
            title="No saved addresses"
            description="Save an address to make checkout faster next time."
            action={<Button onClick={() => setEditing('new')}>Add an address</Button>}
          />
        )
      ) : (
        <ul className="space-y-3">
          {addresses.map((address) => (
            <li key={address.id} className="card-surface p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="font-medium text-ink-800">{address.label || address.fullName}</p>
                    {address.isDefaultShipping && <Badge tone="success">Default</Badge>}
                  </div>

                  <address className="mt-1.5 text-sm not-italic leading-relaxed text-ink-500">
                    {address.fullName}
                    <br />
                    {address.line1}
                    {address.line2 && (
                      <>
                        <br />
                        {address.line2}
                      </>
                    )}
                    <br />
                    {[address.district, address.region].filter(Boolean).join(', ')}
                    <br />
                    {address.phone}
                  </address>
                </div>

                <div className="flex shrink-0 flex-col items-end gap-1.5">
                  <button
                    type="button"
                    onClick={() => startEdit(address)}
                    className="text-xs font-medium text-saffron-600 hover:text-saffron-700"
                  >
                    Edit
                  </button>

                  {!address.isDefaultShipping && (
                    <>
                      <button
                        type="button"
                        onClick={() => setDefault.mutate(address.id)}
                        className="text-xs text-ink-500 hover:text-ink-700"
                      >
                        Make default
                      </button>
                      <button
                        type="button"
                        onClick={() => remove.mutate(address.id)}
                        className="text-xs text-chilli-600 hover:text-chilli-700"
                      >
                        Delete
                      </button>
                    </>
                  )}
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
