import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { slugify } from '../../lib/slug'
import { Alert, Button, Checkbox, Field, Input, Select, Textarea } from '../../ui/primitives'
import { PageHeader } from '../../features/admin/AdminTable'
import { ImageUploader, type ProductImageDraft } from '../../features/admin/ImageUploader'
import { VariantEditor, type OptionDraft, type VariantDraft } from '../../features/admin/VariantEditor'
import { RouteFallback } from '../../app/components/RouteFallback'
import { useAuth } from '../../app/providers/AuthProvider'

/** `AdminProductDetailDto`. */
interface AdminProductDetail {
  id: string
  name: string
  slug: string
  shortDescription?: string
  description?: string
  categoryId?: string
  brandId?: string
  status: number
  badge?: string
  isFeatured: boolean
  featuredOrder: number
  isTrending: boolean
  trendingOrder: number
  isHero: boolean
  heroOrder: number
  metaTitle?: string
  metaDescription?: string
  publishedAt?: string
  tags: string[]
  options: { id: string; name: string; displayOrder: number; values: { id: string; value: string; hexColor?: string; displayOrder: number }[] }[]
  variants: (Omit<VariantDraft, 'optionValues'> & { id: string; optionValues: { option: string; value: string }[] })[]
  images: { id: string; url: string; thumbnailUrl?: string; altText?: string; width: number; height: number; blurHash?: string; isPrimary: boolean; displayOrder: number }[]
}

interface Category {
  id: string
  name: string
  depth: number
  parentId?: string
}

interface Brand {
  id: string
  name: string
}

const STATUS = [
  { value: 0, label: 'Draft — not visible to shoppers' },
  { value: 1, label: 'Active — live on the storefront' },
  { value: 2, label: 'Archived — hidden, history kept' },
] as const

const BLANK_DETAILS = {
  name: '',
  slug: '',
  shortDescription: '',
  description: '',
  categoryId: '',
  brandId: '',
  status: 0,
  badge: '',
  isFeatured: false,
  featuredOrder: 0,
  isTrending: false,
  trendingOrder: 0,
  isHero: false,
  heroOrder: 0,
  metaTitle: '',
  metaDescription: '',
}

type Details = typeof BLANK_DETAILS

/**
 * The product editor.
 *
 * A full page rather than the inline card the smaller admin screens use: a product carries
 * details, options, a variant matrix, images, tags and SEO, and squeezing that above a table
 * would bury the table and make the form scroll inside a scroll.
 */
export default function AdminProductFormPage() {
  const { id } = useParams()
  const isNew = id === undefined
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { can } = useAuth()

  const [details, setDetails] = useState<Details>(BLANK_DETAILS)
  const [options, setOptions] = useState<OptionDraft[]>([])
  const [variants, setVariants] = useState<VariantDraft[]>([])
  const [images, setImages] = useState<ProductImageDraft[]>([])
  const [tags, setTags] = useState('')
  const [slugTouched, setSlugTouched] = useState(false)

  const product = useQuery({
    queryKey: ['admin', 'product', id],
    queryFn: () => api.get<AdminProductDetail>(`/admin/products/${id}`),
    enabled: !isNew,
  })

  const categories = useQuery({
    queryKey: ['admin', 'categories'],
    queryFn: () => api.get<Category[]>('/admin/categories?includeInactive=true'),
  })

  const brands = useQuery({
    queryKey: ['admin', 'brands'],
    queryFn: () => api.get<Brand[]>('/admin/brands?includeInactive=true'),
  })

  // Load the product into the form once. Keyed on the id rather than the object so a background
  // refetch cannot discard edits that are still in progress.
  useEffect(() => {
    const loaded = product.data
    if (!loaded) return

    setDetails({
      name: loaded.name,
      slug: loaded.slug,
      shortDescription: loaded.shortDescription ?? '',
      description: loaded.description ?? '',
      categoryId: loaded.categoryId ?? '',
      brandId: loaded.brandId ?? '',
      status: loaded.status,
      badge: loaded.badge ?? '',
      isFeatured: loaded.isFeatured,
      featuredOrder: loaded.featuredOrder,
      isTrending: loaded.isTrending,
      trendingOrder: loaded.trendingOrder,
      isHero: loaded.isHero,
      heroOrder: loaded.heroOrder,
      metaTitle: loaded.metaTitle ?? '',
      metaDescription: loaded.metaDescription ?? '',
    })

    setOptions(
      loaded.options.map((option) => ({
        id: option.id,
        name: option.name,
        displayOrder: option.displayOrder,
        values: option.values.map((value) => ({
          id: value.id,
          value: value.value,
          hexColor: value.hexColor ?? null,
          displayOrder: value.displayOrder,
        })),
      })),
    )

    setVariants(loaded.variants.map((variant) => ({ ...variant })))

    setImages(
      loaded.images.map((image) => ({
        id: image.id,
        url: image.url,
        thumbnailUrl: image.thumbnailUrl ?? null,
        altText: image.altText ?? null,
        width: image.width,
        height: image.height,
        blurHash: image.blurHash ?? null,
        isPrimary: image.isPrimary,
        displayOrder: image.displayOrder,
      })),
    )

    setTags(loaded.tags.join(', '))
    setSlugTouched(true)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [product.data?.id])

  function patch(next: Partial<Details>) {
    setDetails((current) => ({ ...current, ...next }))
  }

  const slugPreview = details.slug.trim() ? slugify(details.slug) : slugify(details.name)

  const categoryOptions = useMemo(() => categories.data ?? [], [categories.data])

  const save = useMutation({
    mutationFn: () => {
      const tagList = tags
        .split(',')
        .map((tag) => tag.trim())
        .filter(Boolean)

      const common = {
        name: details.name.trim(),
        slug: details.slug.trim() || null,
        shortDescription: details.shortDescription.trim() || null,
        description: details.description.trim() || null,
        categoryId: details.categoryId || null,
        brandId: details.brandId || null,
        status: Number(details.status),
        badge: details.badge.trim() || null,
        isFeatured: details.isFeatured,
        isTrending: details.isTrending,
        isHero: details.isHero,
        metaTitle: details.metaTitle.trim() || null,
        metaDescription: details.metaDescription.trim() || null,
        tags: tagList,
      }

      const imagePayload = images.map((image, index) => ({
        id: image.id ?? null,
        url: image.url,
        thumbnailUrl: image.thumbnailUrl ?? null,
        altText: image.altText?.trim() || null,
        width: image.width,
        height: image.height,
        blurHash: image.blurHash ?? null,
        isPrimary: image.isPrimary,
        displayOrder: index,
      }))

      if (isNew) {
        // Create takes a flatter shape: there is nothing to reconcile yet, and every variant is
        // new, so its stock figure is an opening balance.
        return api.post<{ id: string }>('/admin/products', {
          ...common,
          variants: variants.map((variant, index) => ({
            name: variant.name?.trim() || null,
            sku: variant.sku.trim() || null,
            barcode: variant.barcode?.trim() || null,
            price: variant.price,
            compareAtPrice: variant.compareAtPrice ?? null,
            costPrice: variant.costPrice ?? null,
            stockQuantity: variant.stockQuantity,
            lowStockThreshold: variant.lowStockThreshold,
            trackInventory: variant.trackInventory,
            allowBackorder: variant.allowBackorder,
            weightGrams: variant.weightGrams ?? null,
            unit: variant.unit,
            unitValue: variant.unitValue ?? null,
            isDefault: variant.isDefault,
            displayOrder: index,
          })),
          images: imagePayload,
        })
      }

      return api.put<{ id: string }>(`/admin/products/${id}`, {
        ...common,
        featuredOrder: details.featuredOrder,
        trendingOrder: details.trendingOrder,
        heroOrder: details.heroOrder,
        options: options.map((option, index) => ({
          id: option.id ?? null,
          name: option.name.trim(),
          displayOrder: index,
          values: option.values.map((value, valueIndex) => ({
            id: value.id ?? null,
            value: value.value.trim(),
            hexColor: value.hexColor ?? null,
            displayOrder: valueIndex,
          })),
        })),
        variants: variants.map((variant, index) => ({
          id: variant.id ?? null,
          name: variant.name?.trim() || null,
          sku: variant.sku.trim() || null,
          barcode: variant.barcode?.trim() || null,
          price: variant.price,
          compareAtPrice: variant.compareAtPrice ?? null,
          costPrice: variant.costPrice ?? null,
          stockQuantity: variant.stockQuantity,
          lowStockThreshold: variant.lowStockThreshold,
          trackInventory: variant.trackInventory,
          allowBackorder: variant.allowBackorder,
          weightGrams: variant.weightGrams ?? null,
          unit: variant.unit,
          unitValue: variant.unitValue ?? null,
          isDefault: variant.isDefault,
          isActive: variant.isActive,
          displayOrder: index,
          optionValues: variant.optionValues,
        })),
        images: imagePayload,
      })
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'products'] })
      await queryClient.invalidateQueries({ queryKey: ['admin', 'product', id] })
      await queryClient.invalidateQueries({ queryKey: ['storefront', 'bootstrap'] })

      navigate(isNew ? `/admin/products/${saved.id}/edit` : '/admin/products')
    },
  })

  if (!isNew && product.isLoading) {
    return <RouteFallback />
  }

  if (!isNew && product.isError) {
    return (
      <div>
        <PageHeader title="Product" description="" />
        <Alert tone="error">{(product.error as Error).message}</Alert>
      </div>
    )
  }

  const canSave = isNew ? can('products.create') : can('products.update')
  const blockingIssue =
    variants.length === 0
      ? 'Add at least one variant — price and stock live on the variant, not the product.'
      : !variants.some((variant) => variant.isActive)
        ? 'At least one variant must stay active.'
        : null

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        if (!blockingIssue) save.mutate()
      }}
    >
      <PageHeader
        title={isNew ? 'New product' : details.name || 'Edit product'}
        description={
          isNew
            ? 'Details, then at least one variant. Everything else can be filled in later.'
            : slugPreview
              ? `/product/${slugPreview}`
              : ''
        }
        action={
          <div className="flex gap-2">
            <Link to="/admin/products">
              <Button type="button" variant="ghost">
                Cancel
              </Button>
            </Link>
            <Button type="submit" loading={save.isPending} disabled={!canSave || blockingIssue !== null}>
              {isNew ? 'Create product' : 'Save changes'}
            </Button>
          </div>
        }
      />

      {save.isError && (
        <div className="mb-4">
          <Alert tone="error" title="Could not save">
            {(save.error as Error).message}
          </Alert>
        </div>
      )}

      {blockingIssue && (
        <div className="mb-4">
          <Alert tone="warning">{blockingIssue}</Alert>
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-3">
        {/* ---- Main column ---- */}
        <div className="space-y-4 lg:col-span-2">
          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Details</h2>

            <div className="mt-4 grid gap-3 sm:grid-cols-2">
              <div className="sm:col-span-2">
                <Field
                  label="Name"
                  htmlFor="name"
                  required
                  hint={slugPreview ? `URL: /product/${slugPreview}` : 'Used to build the URL'}
                >
                  <Input
                    id="name"
                    value={details.name}
                    onChange={(event) => {
                      patch({ name: event.target.value })
                      // Keeps the slug in step while it has never been edited by hand, then stops
                      // touching it — renaming a live product must not silently break its URL.
                      if (!slugTouched) patch({ slug: '' })
                    }}
                    required
                    autoFocus
                  />
                </Field>
              </div>

              <Field label="Slug" htmlFor="slug" hint="Leave blank to generate from the name">
                <Input
                  id="slug"
                  value={details.slug}
                  onChange={(event) => {
                    setSlugTouched(true)
                    patch({ slug: event.target.value })
                  }}
                  placeholder={slugify(details.name) || 'auto'}
                />
              </Field>

              <Field label="Badge" htmlFor="badge" hint="Corner ribbon, e.g. “New”">
                <Input
                  id="badge"
                  value={details.badge}
                  onChange={(event) => patch({ badge: event.target.value })}
                />
              </Field>

              <div className="sm:col-span-2">
                <Field
                  label="Short description"
                  htmlFor="short-description"
                  hint="One line, shown on cards and in search results"
                >
                  <Input
                    id="short-description"
                    value={details.shortDescription}
                    onChange={(event) => patch({ shortDescription: event.target.value })}
                  />
                </Field>
              </div>

              <div className="sm:col-span-2">
                <Field label="Description" htmlFor="description" hint="Sanitised on save">
                  <Textarea
                    id="description"
                    rows={6}
                    value={details.description}
                    onChange={(event) => patch({ description: event.target.value })}
                  />
                </Field>
              </div>
            </div>
          </section>

          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Options &amp; variants</h2>
            <div className="mt-4">
              <VariantEditor
                options={options}
                variants={variants}
                onOptionsChange={setOptions}
                onVariantsChange={setVariants}
                isNewProduct={isNew}
              />
            </div>
          </section>

          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Images</h2>
            <p className="mt-0.5 text-xs text-ink-400">
              The primary image is the one the catalogue card uses.
            </p>
            <div className="mt-4">
              <ImageUploader images={images} onChange={setImages} disabled={!can('media.upload')} />
            </div>
          </section>

          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Search engines</h2>

            <div className="mt-4 grid gap-3">
              <Field label="Meta title" htmlFor="meta-title" hint="Falls back to the product name">
                <Input
                  id="meta-title"
                  value={details.metaTitle}
                  onChange={(event) => patch({ metaTitle: event.target.value })}
                />
              </Field>

              <Field
                label="Meta description"
                htmlFor="meta-description"
                hint="The snippet under the link in search results"
              >
                <Textarea
                  id="meta-description"
                  rows={3}
                  value={details.metaDescription}
                  onChange={(event) => patch({ metaDescription: event.target.value })}
                />
              </Field>
            </div>
          </section>
        </div>

        {/* ---- Sidebar ---- */}
        <div className="space-y-4">
          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Publishing</h2>

            <div className="mt-4 space-y-3">
              <Field label="Status" htmlFor="status">
                <Select
                  id="status"
                  value={details.status}
                  onChange={(event) => patch({ status: Number(event.target.value) })}
                >
                  {STATUS.map((status) => (
                    <option key={status.value} value={status.value}>
                      {status.label}
                    </option>
                  ))}
                </Select>
              </Field>

              <Field label="Category" htmlFor="category">
                <Select
                  id="category"
                  value={details.categoryId}
                  onChange={(event) => patch({ categoryId: event.target.value })}
                >
                  <option value="">— None —</option>
                  {categoryOptions.map((category) => (
                    <option key={category.id} value={category.id}>
                      {'  '.repeat(category.depth)}
                      {category.depth > 0 ? '└ ' : ''}
                      {category.name}
                    </option>
                  ))}
                </Select>
              </Field>

              <Field label="Brand" htmlFor="brand">
                <Select
                  id="brand"
                  value={details.brandId}
                  onChange={(event) => patch({ brandId: event.target.value })}
                >
                  <option value="">— None —</option>
                  {(brands.data ?? []).map((brand) => (
                    <option key={brand.id} value={brand.id}>
                      {brand.name}
                    </option>
                  ))}
                </Select>
              </Field>

              <Field label="Tags" htmlFor="tags" hint="Comma separated">
                <Input
                  id="tags"
                  value={tags}
                  onChange={(event) => setTags(event.target.value)}
                  placeholder="basmati, long grain"
                />
              </Field>
            </div>
          </section>

          <section className="card-surface p-5">
            <h2 className="font-display text-base font-bold text-ink-900">Merchandising</h2>
            <p className="mt-0.5 text-xs text-ink-400">Where this product surfaces on the home page.</p>

            <div className="mt-3 space-y-1">
              <Checkbox
                label="Featured"
                checked={details.isFeatured}
                onChange={(event) => patch({ isFeatured: event.target.checked })}
              />
              <Checkbox
                label="Trending"
                checked={details.isTrending}
                onChange={(event) => patch({ isTrending: event.target.checked })}
              />
              <Checkbox
                label="Hero"
                checked={details.isHero}
                onChange={(event) => patch({ isHero: event.target.checked })}
              />
            </div>
          </section>

          {!isNew && product.data && (
            <section className="card-surface p-5 text-xs text-ink-500">
              <p>
                {product.data.publishedAt
                  ? `First published ${new Date(product.data.publishedAt).toLocaleDateString()}`
                  : 'Never published'}
              </p>
              <Link
                to={`/product/${product.data.slug}`}
                target="_blank"
                rel="noopener"
                className="mt-2 inline-block font-medium text-saffron-600 hover:text-saffron-700"
              >
                View on the storefront ↗
              </Link>
            </section>
          )}
        </div>
      </div>
    </form>
  )
}
