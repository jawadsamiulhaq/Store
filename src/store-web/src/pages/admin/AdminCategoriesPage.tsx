import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { slugify } from '../../lib/slug'
import { Alert, Badge, Button, Checkbox, Field, Input, Select, Textarea } from '../../ui/primitives'
import { PageHeader, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { useAuth } from '../../app/providers/AuthProvider'

interface Category {
  id: string
  name: string
  slug: string
  description?: string
  imageUrl?: string
  iconName?: string
  parentId?: string
  depth: number
  displayOrder: number
  isActive: boolean
  showInMenu: boolean
  productCount: number
  metaTitle?: string
  metaDescription?: string
}

const BLANK = {
  name: '',
  slug: '',
  description: '',
  imageUrl: '',
  iconName: '',
  parentId: '',
  displayOrder: 0,
  isActive: true,
  showInMenu: true,
  metaTitle: '',
  metaDescription: '',
}

type Form = typeof BLANK

/**
 * Orders a flat category list into tree order — every node immediately followed by its subtree.
 *
 * The API returns rows sorted by depth, so all roots come first, then all depth-1 nodes, and so
 * on. Indenting that by `depth` directly would render a child several rows below a parent it has
 * nothing to do with, which reads as a broken hierarchy rather than a flat one.
 */
function toTreeOrder(rows: Category[]): Category[] {
  const byParent = new Map<string, Category[]>()

  for (const row of rows) {
    const key = row.parentId ?? ''
    const siblings = byParent.get(key)
    if (siblings) siblings.push(row)
    else byParent.set(key, [row])
  }

  for (const siblings of byParent.values()) {
    siblings.sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name))
  }

  const ordered: Category[] = []

  // Iterative rather than recursive: a corrupted parent chain would blow the stack, and an
  // explicit visited set also makes an accidental cycle render once instead of hanging the tab.
  const visited = new Set<string>()

  function walk(parentKey: string) {
    for (const row of byParent.get(parentKey) ?? []) {
      if (visited.has(row.id)) continue
      visited.add(row.id)
      ordered.push(row)
      walk(row.id)
    }
  }

  walk('')

  // Anything whose parent is missing (inactive, deleted, or filtered out) would otherwise vanish
  // from the table entirely. Appending the orphans keeps the list honest about what exists.
  for (const row of rows) {
    if (!visited.has(row.id)) ordered.push(row)
  }

  return ordered
}

/** Ids that may not be chosen as a parent: the category itself and everything beneath it. */
function descendantIds(rows: Category[], rootId: string): Set<string> {
  const blocked = new Set([rootId])
  let grew = true

  // The list is depth-ordered but a single pass is not enough once orphans are appended, so this
  // repeats until it stops finding new members.
  while (grew) {
    grew = false
    for (const row of rows) {
      if (row.parentId && blocked.has(row.parentId) && !blocked.has(row.id)) {
        blocked.add(row.id)
        grew = true
      }
    }
  }

  return blocked
}

export default function AdminCategoriesPage() {
  const queryClient = useQueryClient()
  const { can } = useAuth()

  const [editing, setEditing] = useState<Category | 'new' | null>(null)
  const [form, setForm] = useState<Form>(BLANK)

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'categories'],
    queryFn: () => api.get<Category[]>('/admin/categories?includeInactive=true'),
  })

  const rows = useMemo(() => toTreeOrder(data ?? []), [data])

  // A category cannot be moved inside itself — the server rejects it, and offering the choice at
  // all only invites the error.
  const parentOptions = useMemo(() => {
    if (editing === null || editing === 'new') return rows
    const blocked = descendantIds(data ?? [], editing.id)
    return rows.filter((row) => !blocked.has(row.id))
  }, [rows, data, editing])

  function patch(next: Partial<Form>) {
    setForm((current) => ({ ...current, ...next }))
  }

  const save = useMutation({
    mutationFn: () => {
      const payload = {
        name: form.name.trim(),
        slug: form.slug.trim() || null,
        description: form.description.trim() || null,
        imageUrl: form.imageUrl.trim() || null,
        iconName: form.iconName.trim() || null,
        parentId: form.parentId || null,
        displayOrder: Number(form.displayOrder) || 0,
        isActive: form.isActive,
        showInMenu: form.showInMenu,
        metaTitle: form.metaTitle.trim() || null,
        metaDescription: form.metaDescription.trim() || null,
      }

      return editing === 'new'
        ? api.post<Category>('/admin/categories', payload)
        : api.put<Category>(`/admin/categories/${(editing as Category).id}`, payload)
    },
    onSuccess: async () => {
      setEditing(null)
      setForm(BLANK)
      // The storefront menu and the catalogue both read categories, so both caches are stale.
      await queryClient.invalidateQueries({ queryKey: ['admin', 'categories'] })
      await queryClient.invalidateQueries({ queryKey: ['storefront', 'bootstrap'] })
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.del<void>(`/admin/categories/${id}`),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'categories'] })
      await queryClient.invalidateQueries({ queryKey: ['storefront', 'bootstrap'] })
    },
  })

  const refreshCounts = useMutation({
    mutationFn: () => api.post<void>('/admin/categories/refresh-counts'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'categories'] }),
  })

  function startEdit(category: Category) {
    setForm({
      name: category.name,
      slug: category.slug,
      description: category.description ?? '',
      imageUrl: category.imageUrl ?? '',
      iconName: category.iconName ?? '',
      parentId: category.parentId ?? '',
      displayOrder: category.displayOrder,
      isActive: category.isActive,
      showInMenu: category.showInMenu,
      metaTitle: category.metaTitle ?? '',
      metaDescription: category.metaDescription ?? '',
    })
    setEditing(category)
  }

  function startCreate() {
    setForm(BLANK)
    setEditing('new')
  }

  // Shown under the name field so the admin can see the URL before committing to it.
  const slugPreview = form.slug.trim() ? slugify(form.slug) : slugify(form.name)

  return (
    <div>
      <PageHeader
        title="Categories"
        description="The aisle tree. A category's URL, its place in the menu and its parent all live here."
        action={
          can('categories.create') && editing === null ? (
            <Button onClick={startCreate}>New category</Button>
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
            {editing === 'new' ? 'New category' : `Edit ${(editing as Category).name}`}
          </h2>

          {save.isError && (
            <div className="mt-3">
              <Alert tone="error">{(save.error as Error).message}</Alert>
            </div>
          )}

          <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <Field
              label="Name"
              htmlFor="cat-name"
              required
              hint={slugPreview ? `URL: /category/${slugPreview}` : 'Used to build the URL'}
            >
              <Input
                id="cat-name"
                value={form.name}
                onChange={(event) => patch({ name: event.target.value })}
                required
                autoFocus
              />
            </Field>

            <Field label="Slug" htmlFor="cat-slug" hint="Leave blank to generate from the name">
              <Input
                id="cat-slug"
                value={form.slug}
                onChange={(event) => patch({ slug: event.target.value })}
                placeholder={slugify(form.name) || 'auto'}
              />
            </Field>

            <Field label="Parent" htmlFor="cat-parent" hint="Blank makes it a top-level aisle">
              <Select
                id="cat-parent"
                value={form.parentId}
                onChange={(event) => patch({ parentId: event.target.value })}
              >
                <option value="">— No parent —</option>
                {parentOptions.map((option) => (
                  <option key={option.id} value={option.id}>
                    {'  '.repeat(option.depth)}
                    {option.depth > 0 ? '└ ' : ''}
                    {option.name}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Display order" htmlFor="cat-order" hint="Lower sorts first among siblings">
              <Input
                id="cat-order"
                type="number"
                value={form.displayOrder}
                onChange={(event) => patch({ displayOrder: Number(event.target.value) })}
              />
            </Field>

            <Field label="Icon name" htmlFor="cat-icon" hint="Optional menu glyph key">
              <Input
                id="cat-icon"
                value={form.iconName}
                onChange={(event) => patch({ iconName: event.target.value })}
                placeholder="e.g. rice"
              />
            </Field>

            <Field label="Image URL" htmlFor="cat-image" hint="Shown on the aisle tile">
              <Input
                id="cat-image"
                value={form.imageUrl}
                onChange={(event) => patch({ imageUrl: event.target.value })}
                placeholder="/uploads/…"
              />
            </Field>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Description" htmlFor="cat-description">
              <Textarea
                id="cat-description"
                rows={3}
                value={form.description}
                onChange={(event) => patch({ description: event.target.value })}
                placeholder="Shown at the top of the category page"
              />
            </Field>

            <Field label="Meta description" htmlFor="cat-meta-description" hint="Search-result snippet">
              <Textarea
                id="cat-meta-description"
                rows={3}
                value={form.metaDescription}
                onChange={(event) => patch({ metaDescription: event.target.value })}
              />
            </Field>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Meta title" htmlFor="cat-meta-title" hint="Falls back to the name when blank">
              <Input
                id="cat-meta-title"
                value={form.metaTitle}
                onChange={(event) => patch({ metaTitle: event.target.value })}
              />
            </Field>

            <div className="flex flex-wrap items-center gap-2 pt-6">
              <Checkbox
                label="Active"
                checked={form.isActive}
                onChange={(event) => patch({ isActive: event.target.checked })}
              />
              <Checkbox
                label="Show in menu"
                checked={form.showInMenu}
                onChange={(event) => patch({ showInMenu: event.target.checked })}
              />
            </div>
          </div>

          <div className="mt-5 flex gap-2">
            <Button type="submit" loading={save.isPending}>
              {editing === 'new' ? 'Create category' : 'Save category'}
            </Button>
            <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
              Cancel
            </Button>
          </div>
        </form>
      )}

      {remove.isError && (
        <div className="mb-3">
          <Alert tone="error">{(remove.error as Error).message}</Alert>
        </div>
      )}

      {isLoading ? (
        <TableSkeleton columns={5} />
      ) : (
        <>
          <TableShell>
            <thead>
              <tr>
                <Th>Category</Th>
                <Th>URL</Th>
                <Th align="right">Products</Th>
                <Th>Visibility</Th>
                <Th align="right"> </Th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && <TableEmpty colSpan={5} message="No categories yet." />}

              {rows.map((category) => (
                <tr key={category.id} className="transition-colors hover:bg-ink-50/60">
                  <Td>
                    {/* Indent carries the hierarchy. Padding rather than nested markup, so the
                        table keeps one row per category and stays sortable by the browser. */}
                    <div style={{ paddingLeft: `${category.depth * 1.25}rem` }} className="flex items-center gap-2">
                      {category.depth > 0 && (
                        <span className="text-ink-300" aria-hidden="true">
                          └
                        </span>
                      )}
                      <span className="font-medium text-ink-800">{category.name}</span>
                      {category.displayOrder !== 0 && (
                        <span className="text-[10px] text-ink-400">#{category.displayOrder}</span>
                      )}
                    </div>
                  </Td>

                  <Td className="font-mono text-[11px] text-ink-500">/{category.slug}</Td>

                  <Td align="right" className="text-ink-700">
                    {category.productCount}
                  </Td>

                  <Td>
                    <div className="flex flex-wrap gap-1">
                      <Badge tone={category.isActive ? 'success' : 'neutral'}>
                        {category.isActive ? 'Active' : 'Hidden'}
                      </Badge>
                      {category.showInMenu && <Badge tone="neutral">In menu</Badge>}
                    </div>
                  </Td>

                  <Td align="right">
                    <div className="flex justify-end gap-1">
                      {can('categories.update') && (
                        <Button variant="ghost" size="sm" onClick={() => startEdit(category)}>
                          Edit
                        </Button>
                      )}
                      {can('categories.delete') && (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="text-chilli-600"
                          onClick={() => {
                            if (window.confirm(`Delete "${category.name}"?`)) {
                              remove.mutate(category.id)
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

          {can('categories.update') && (
            <div className="mt-3 flex items-center gap-3">
              <Button
                variant="outline"
                size="sm"
                loading={refreshCounts.isPending}
                onClick={() => refreshCounts.mutate()}
              >
                Refresh product counts
              </Button>
              <p className="text-xs text-ink-400">
                Counts are maintained, not live. Recompute after a bulk import or a batch of
                reassignments.
              </p>
            </div>
          )}
        </>
      )}
    </div>
  )
}
