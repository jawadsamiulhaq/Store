import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { slugify } from '../../lib/slug'
import { Alert, Badge, Button, Checkbox, Field, Input, Textarea } from '../../ui/primitives'
import { FilterBar, PageHeader, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { useAuth } from '../../app/providers/AuthProvider'

interface Brand {
  id: string
  name: string
  slug: string
  description?: string
  logoUrl?: string
  countryOfOrigin?: string
  displayOrder: number
  isActive: boolean
  isFeatured: boolean
  productCount: number
}

const BLANK = {
  name: '',
  slug: '',
  description: '',
  logoUrl: '',
  countryOfOrigin: '',
  displayOrder: 0,
  isActive: true,
  isFeatured: false,
}

type Form = typeof BLANK

export default function AdminBrandsPage() {
  const queryClient = useQueryClient()
  const { can } = useAuth()

  const [editing, setEditing] = useState<Brand | 'new' | null>(null)
  const [form, setForm] = useState<Form>(BLANK)
  const [search, setSearch] = useState('')

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'brands'],
    queryFn: () => api.get<Brand[]>('/admin/brands?includeInactive=true'),
  })

  // Filtered in the browser rather than on the server: the endpoint takes no search parameter, and
  // a provision store has tens of brands, not thousands. If that stops being true this becomes a
  // query parameter instead.
  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()
    const all = data ?? []
    if (!term) return all
    return all.filter(
      (brand) =>
        brand.name.toLowerCase().includes(term) ||
        brand.slug.includes(term) ||
        (brand.countryOfOrigin?.toLowerCase().includes(term) ?? false),
    )
  }, [data, search])

  function patch(next: Partial<Form>) {
    setForm((current) => ({ ...current, ...next }))
  }

  const save = useMutation({
    mutationFn: () => {
      const payload = {
        name: form.name.trim(),
        slug: form.slug.trim() || null,
        description: form.description.trim() || null,
        logoUrl: form.logoUrl.trim() || null,
        countryOfOrigin: form.countryOfOrigin.trim() || null,
        displayOrder: Number(form.displayOrder) || 0,
        isActive: form.isActive,
        isFeatured: form.isFeatured,
      }

      return editing === 'new'
        ? api.post<Brand>('/admin/brands', payload)
        : api.put<Brand>(`/admin/brands/${(editing as Brand).id}`, payload)
    },
    onSuccess: async () => {
      setEditing(null)
      setForm(BLANK)
      await queryClient.invalidateQueries({ queryKey: ['admin', 'brands'] })
      await queryClient.invalidateQueries({ queryKey: ['storefront', 'bootstrap'] })
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.del<void>(`/admin/brands/${id}`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'brands'] })
      await queryClient.invalidateQueries({ queryKey: ['storefront', 'bootstrap'] })
    },
  })

  function startEdit(brand: Brand) {
    setForm({
      name: brand.name,
      slug: brand.slug,
      description: brand.description ?? '',
      logoUrl: brand.logoUrl ?? '',
      countryOfOrigin: brand.countryOfOrigin ?? '',
      displayOrder: brand.displayOrder,
      isActive: brand.isActive,
      isFeatured: brand.isFeatured,
    })
    setEditing(brand)
  }

  const slugPreview = form.slug.trim() ? slugify(form.slug) : slugify(form.name)

  return (
    <div>
      <PageHeader
        title="Brands"
        description="Who makes what. A product's brand drives its filter facet and its /brand page."
        action={
          can('brands.create') && editing === null ? (
            <Button
              onClick={() => {
                setForm(BLANK)
                setEditing('new')
              }}
            >
              New brand
            </Button>
          ) : undefined
        }
      />

      {editing !== null && (
        <form
          onSubmit={(event) => {
            event.preventDefault()
            save.mutate()
          }}
          className="card-surface animate-fade-rise mb-5 p-5"
        >
          <h2 className="font-display text-base font-bold text-ink-900">
            {editing === 'new' ? 'New brand' : `Edit ${(editing as Brand).name}`}
          </h2>

          {save.isError && (
            <div className="mt-3">
              <Alert tone="error">{(save.error as Error).message}</Alert>
            </div>
          )}

          <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <Field
              label="Name"
              htmlFor="brand-name"
              required
              hint={slugPreview ? `URL: /brand/${slugPreview}` : 'Used to build the URL'}
            >
              <Input
                id="brand-name"
                value={form.name}
                onChange={(event) => patch({ name: event.target.value })}
                required
                autoFocus
              />
            </Field>

            <Field label="Slug" htmlFor="brand-slug" hint="Leave blank to generate from the name">
              <Input
                id="brand-slug"
                value={form.slug}
                onChange={(event) => patch({ slug: event.target.value })}
                placeholder={slugify(form.name) || 'auto'}
              />
            </Field>

            <Field label="Country of origin" htmlFor="brand-country" hint="Shown on the product page">
              <Input
                id="brand-country"
                value={form.countryOfOrigin}
                onChange={(event) => patch({ countryOfOrigin: event.target.value })}
                placeholder="e.g. Pakistan"
              />
            </Field>

            <Field label="Logo URL" htmlFor="brand-logo">
              <Input
                id="brand-logo"
                value={form.logoUrl}
                onChange={(event) => patch({ logoUrl: event.target.value })}
                placeholder="/uploads/…"
              />
            </Field>

            <Field label="Display order" htmlFor="brand-order" hint="Lower sorts first">
              <Input
                id="brand-order"
                type="number"
                value={form.displayOrder}
                onChange={(event) => patch({ displayOrder: Number(event.target.value) })}
              />
            </Field>

            <div className="flex flex-wrap items-center gap-2 pt-6">
              <Checkbox
                label="Active"
                checked={form.isActive}
                onChange={(event) => patch({ isActive: event.target.checked })}
              />
              <Checkbox
                label="Featured"
                checked={form.isFeatured}
                onChange={(event) => patch({ isFeatured: event.target.checked })}
              />
            </div>
          </div>

          <Field label="Description" htmlFor="brand-description">
            <Textarea
              id="brand-description"
              rows={3}
              value={form.description}
              onChange={(event) => patch({ description: event.target.value })}
              placeholder="Shown at the top of the brand page"
            />
          </Field>

          <div className="mt-2 flex gap-2">
            <Button type="submit" loading={save.isPending}>
              {editing === 'new' ? 'Create brand' : 'Save brand'}
            </Button>
            <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
          </div>
        </form>
      )}

      <FilterBar>
        <Input
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Filter by name, slug or country…"
          className="h-9 w-72"
          aria-label="Filter brands"
        />
        <span className="text-xs text-ink-400">
          {rows.length} of {data?.length ?? 0}
        </span>
      </FilterBar>

      {remove.isError && (
        <div className="mb-3">
          <Alert tone="error">{(remove.error as Error).message}</Alert>
        </div>
      )}

      {isLoading ? (
        <TableSkeleton columns={5} />
      ) : (
        <TableShell>
          <thead>
            <tr>
              <Th>Brand</Th>
              <Th>URL</Th>
              <Th>Origin</Th>
              <Th align="right">Products</Th>
              <Th>Status</Th>
              <Th align="right"> </Th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 && (
              <TableEmpty
                colSpan={6}
                message={search ? 'No brands match that filter.' : 'No brands yet.'}
              />
            )}

            {rows.map((brand) => (
              <tr key={brand.id} className="transition-colors hover:bg-ink-50/60">
                <Td>
                  <div className="flex items-center gap-3">
                    {/* Plain <img>, not the <Image> placeholder component: a missing brand logo
                        should read as "no logo" rather than as a generated product tile. */}
                    {brand.logoUrl ? (
                      <img
                        src={brand.logoUrl}
                        alt=""
                        width={32}
                        height={32}
                        loading="lazy"
                        className="h-8 w-8 shrink-0 rounded-lg object-contain"
                      />
                    ) : (
                      <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-ink-100 text-[10px] font-bold text-ink-400">
                        {brand.name.slice(0, 2).toUpperCase()}
                      </span>
                    )}
                    <span className="font-medium text-ink-800">{brand.name}</span>
                  </div>
                </Td>

                <Td className="font-mono text-[11px] text-ink-500">/{brand.slug}</Td>

                <Td className="text-xs text-ink-500">{brand.countryOfOrigin ?? '—'}</Td>

                <Td align="right" className="text-ink-700">
                  {brand.productCount}
                </Td>

                <Td>
                  <div className="flex flex-wrap gap-1">
                    <Badge tone={brand.isActive ? 'success' : 'neutral'}>
                      {brand.isActive ? 'Active' : 'Hidden'}
                    </Badge>
                    {brand.isFeatured && <Badge tone="new">Featured</Badge>}
                  </div>
                </Td>

                <Td align="right">
                  <div className="flex justify-end gap-1">
                    {can('brands.update') && (
                      <Button variant="ghost" size="sm" onClick={() => startEdit(brand)}>
                        Edit
                      </Button>
                    )}
                    {can('brands.delete') && (
                      <Button
                        variant="ghost"
                        size="sm"
                        className="text-chilli-600"
                        onClick={() => {
                          if (window.confirm(`Delete "${brand.name}"?`)) {
                            remove.mutate(brand.id)
                          }
                        }}
                      >
                        Delete
                      </Button>
                    )}
                  </div>
                </Td>
              </tr>
            ))}
          </tbody>
        </TableShell>
      )}
    </div>
  )
}
