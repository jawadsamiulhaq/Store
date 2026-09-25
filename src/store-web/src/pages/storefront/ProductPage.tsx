import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Image } from '../../ui/Image'
import { Alert, Badge, Button, EmptyState, Rating, Spinner } from '../../ui/primitives'
import { ProductCard } from '../../features/catalog/ProductCard'
import { useProduct, useProductReviews, useRelatedProducts } from '../../features/catalog/useCatalog'
import { useCartMutations } from '../../features/cart/useCart'
import { useWishlistIds, useWishlistToggle } from '../../features/wishlist/useWishlist'
import { formatPrice, formatPricePerUnit, formatRelative, formatUnit } from '../../lib/format'
import type { ProductVariant } from '../../lib/types'

export default function ProductPage() {
  const { slug } = useParams()
  const { data: product, isLoading, isError } = useProduct(slug)
  const { data: related } = useRelatedProducts(product?.id)
  const { data: reviews } = useProductReviews(product?.id)
  const { ids: savedIds } = useWishlistIds()
  const toggleWishlist = useWishlistToggle()
  const { addItem } = useCartMutations()

  const [variantId, setVariantId] = useState<string | null>(null)
  const [imageIndex, setImageIndex] = useState(0)
  const [quantity, setQuantity] = useState(1)
  const [added, setAdded] = useState(false)

  // Reset when navigating between products, otherwise a stale variant/image index leaks across.
  useEffect(() => {
    setVariantId(null)
    setImageIndex(0)
    setQuantity(1)
  }, [slug])

  // Sets the page title for history, sharing and SEO. This is a client-rendered app, so the
  // title has to be updated imperatively rather than by the server.
  useEffect(() => {
    if (product) {
      document.title = `${product.metaTitle ?? product.name} — Waqas Provision Store`
    }

    return () => {
      document.title = 'Waqas Provision Store — Groceries delivered across Hong Kong'
    }
  }, [product])

  const selectedVariant = useMemo<ProductVariant | undefined>(() => {
    if (!product) return undefined
    return (
      product.variants.find((variant) => variant.id === variantId) ??
      product.variants.find((variant) => variant.isDefault) ??
      product.variants[0]
    )
  }, [product, variantId])

  if (isLoading) {
    return <ProductSkeleton />
  }

  if (isError || !product) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-16">
        <EmptyState
          title="We could not find that product"
          description="It may have been removed, or the link may be out of date."
          action={
            <Link to="/shop" className="text-sm font-medium text-saffron-600 hover:text-saffron-700">
              Browse all products →
            </Link>
          }
        />
      </div>
    )
  }

  const maxQuantity = Math.min(selectedVariant?.availableQuantity ?? 1, 99)
  const isSellable = selectedVariant?.isSellable ?? false
  const activeImage = product.images[imageIndex] ?? product.images[0]
  const isSaved = savedIds.has(product.id)

  async function handleAddToCart() {
    if (!selectedVariant) return

    try {
      await addItem.mutateAsync({ productVariantId: selectedVariant.id, quantity })
      setAdded(true)
      window.setTimeout(() => setAdded(false), 2000)
    } catch {
      // The alert below surfaces the failure from the mutation state.
    }
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <nav aria-label="Breadcrumb" className="flex flex-wrap items-center gap-1.5 text-xs text-ink-400">
        <Link to="/" className="hover:text-saffron-600">
          Home
        </Link>
        {product.breadcrumbs.map((crumb) => (
          <span key={crumb.slug} className="flex items-center gap-1.5">
            <span>/</span>
            <Link to={`/category/${crumb.slug}`} className="hover:text-saffron-600">
              {crumb.name}
            </Link>
          </span>
        ))}
        <span>/</span>
        <span className="truncate text-ink-600">{product.name}</span>
      </nav>

      <div className="mt-6 grid gap-8 lg:grid-cols-2 lg:gap-12">
        {/* ---- Gallery ---- */}
        <div>
          <div className="card-surface overflow-hidden">
            <Image
              src={activeImage?.url}
              alt={activeImage?.altText ?? product.name}
              width={activeImage?.width || 800}
              height={activeImage?.height || 800}
              blurHash={activeImage?.blurHash}
              // The LCP element on this page, so it is eager and high priority.
              priority
              sizes="(min-width: 1024px) 45vw, 92vw"
              className="w-full"
            />
          </div>

          {product.images.length > 1 && (
            <div className="no-scrollbar mt-3 flex gap-2 overflow-x-auto pb-1">
              {product.images.map((image, index) => (
                <button
                  key={image.id}
                  type="button"
                  onClick={() => setImageIndex(index)}
                  aria-label={`View image ${index + 1} of ${product.images.length}`}
                  aria-current={index === imageIndex}
                  className={`shrink-0 overflow-hidden rounded-lg border-2 transition-colors ${
                    index === imageIndex ? 'border-saffron-500' : 'border-transparent hover:border-ink-200'
                  }`}
                >
                  <Image
                    src={image.thumbnailUrl ?? image.url}
                    alt=""
                    width={72}
                    height={72}
                    className="h-18 w-18"
                  />
                </button>
              ))}
            </div>
          )}
        </div>

        {/* ---- Buy box ---- */}
        <div>
          {product.brandName && (
            <Link
              to={`/brand/${product.brandSlug}`}
              className="text-xs font-semibold uppercase tracking-wide text-saffron-600 hover:text-saffron-700"
            >
              {product.brandName}
            </Link>
          )}

          <h1 className="mt-1.5 text-2xl font-bold leading-tight tracking-tight text-ink-900 sm:text-3xl">
            {product.name}
          </h1>

          <div className="mt-3 flex flex-wrap items-center gap-3">
            {product.ratingCount > 0 ? (
              <>
                <Rating value={product.ratingAverage} size="md" />
                <span className="text-sm text-ink-500">
                  {product.ratingAverage.toFixed(1)} · {product.ratingCount} review
                  {product.ratingCount === 1 ? '' : 's'}
                </span>
              </>
            ) : (
              <span className="text-sm text-ink-400">No reviews yet</span>
            )}

            {product.badge && <Badge tone={product.badge === 'Sale' ? 'sale' : 'new'}>{product.badge}</Badge>}
          </div>

          {product.shortDescription && (
            <p className="mt-4 leading-relaxed text-ink-600">{product.shortDescription}</p>
          )}

          {/* ---- Price ---- */}
          <div className="mt-6 flex flex-wrap items-baseline gap-3">
            <span className="text-3xl font-bold tracking-tight text-ink-900">
              {formatPrice(selectedVariant?.price ?? product.minPrice)}
            </span>

            {selectedVariant?.compareAtPrice !== undefined &&
              selectedVariant.compareAtPrice > selectedVariant.price && (
                <>
                  <span className="text-lg text-ink-400 line-through">
                    {formatPrice(selectedVariant.compareAtPrice)}
                  </span>
                  <Badge tone="sale">
                    Save {formatPrice(selectedVariant.compareAtPrice - selectedVariant.price)}
                  </Badge>
                </>
              )}
          </div>

          {/* Unit price, so a shopper can compare a 500 g pack with a 1 kg one honestly. */}
          {selectedVariant && (
            <p className="mt-1 text-sm text-ink-500">
              {formatPricePerUnit(selectedVariant.pricePerUnit, selectedVariant.unit) ??
                (selectedVariant.unitValue
                  ? formatUnit(selectedVariant.unit, selectedVariant.unitValue)
                  : '')}
            </p>
          )}

          {/* ---- Variant picker ---- */}
          {product.variants.length > 1 && (
            <fieldset className="mt-6">
              <legend className="mb-2 text-sm font-medium text-ink-700">Choose a size</legend>
              <div className="flex flex-wrap gap-2">
                {product.variants.map((variant) => {
                  const isSelected = variant.id === selectedVariant?.id

                  return (
                    <button
                      key={variant.id}
                      type="button"
                      onClick={() => {
                        setVariantId(variant.id)
                        setQuantity(1)
                      }}
                      disabled={!variant.isSellable}
                      aria-pressed={isSelected}
                      className={`rounded-xl border px-4 py-2.5 text-left transition-colors ${
                        isSelected
                          ? 'border-saffron-500 bg-saffron-50'
                          : 'border-ink-200 bg-paper-raised hover:border-ink-300'
                      } ${!variant.isSellable ? 'cursor-not-allowed opacity-40' : ''}`}
                    >
                      <span className="block text-sm font-medium text-ink-800">
                        {/* Parenthesised: `??` and `||` cannot be mixed without them, and the
                            intent is "the variant's name, else its formatted unit, else Standard". */}
                        {variant.name ?? (formatUnit(variant.unit, variant.unitValue) || 'Standard')}
                      </span>
                      <span className="block text-xs text-ink-500">{formatPrice(variant.price)}</span>
                      {!variant.isSellable && <span className="block text-[10px] text-chilli-600">Sold out</span>}
                    </button>
                  )
                })}
              </div>
            </fieldset>
          )}

          {/* ---- Stock ---- */}
          <div className="mt-5">
            {!isSellable ? (
              <p className="flex items-center gap-1.5 text-sm font-medium text-chilli-600">
                <span className="h-2 w-2 rounded-full bg-chilli-500" aria-hidden="true" />
                Out of stock
              </p>
            ) : selectedVariant?.isLowStock ? (
              <p className="flex items-center gap-1.5 text-sm font-medium text-saffron-700">
                <span className="h-2 w-2 rounded-full bg-saffron-500" aria-hidden="true" />
                Only {selectedVariant.availableQuantity} left
              </p>
            ) : (
              <p className="flex items-center gap-1.5 text-sm font-medium text-cardamom-600">
                <span className="h-2 w-2 rounded-full bg-cardamom-500" aria-hidden="true" />
                In stock
              </p>
            )}
          </div>

          {/* ---- Add to cart ---- */}
          <div className="mt-5 flex flex-wrap items-stretch gap-3">
            <div className="flex items-center rounded-lg border border-ink-200 bg-paper-raised">
              <button
                type="button"
                onClick={() => setQuantity((value) => Math.max(1, value - 1))}
                disabled={quantity <= 1}
                aria-label="Decrease quantity"
                className="grid h-11 w-11 place-items-center rounded-l-lg text-ink-600 transition-colors hover:bg-ink-50 disabled:opacity-40"
              >
                −
              </button>
              <span className="w-10 text-center text-sm font-semibold text-ink-800" aria-live="polite">
                {quantity}
              </span>
              <button
                type="button"
                onClick={() => setQuantity((value) => Math.min(maxQuantity, value + 1))}
                disabled={quantity >= maxQuantity}
                aria-label="Increase quantity"
                className="grid h-11 w-11 place-items-center rounded-r-lg text-ink-600 transition-colors hover:bg-ink-50 disabled:opacity-40"
              >
                +
              </button>
            </div>

            <Button
              onClick={handleAddToCart}
              disabled={!isSellable || addItem.isPending}
              loading={addItem.isPending}
              size="lg"
              className={`flex-1 ${added ? 'bg-cardamom-500 hover:bg-cardamom-600' : ''}`}
            >
              {added ? 'Added to cart' : isSellable ? 'Add to cart' : 'Out of stock'}
            </Button>

            <button
              type="button"
              onClick={() => toggleWishlist.mutate(product.id)}
              aria-label={isSaved ? 'Remove from saved items' : 'Save for later'}
              aria-pressed={isSaved}
              className="grid h-12 w-12 place-items-center rounded-lg border border-ink-200 bg-paper-raised text-ink-500 transition-colors hover:border-chilli-200 hover:text-chilli-500"
            >
              {toggleWishlist.isPending ? (
                <Spinner />
              ) : (
                <svg
                  className={`h-5 w-5 ${isSaved ? 'text-chilli-500' : ''}`}
                  viewBox="0 0 24 24"
                  fill={isSaved ? 'currentColor' : 'none'}
                  stroke="currentColor"
                  strokeWidth="1.8"
                  aria-hidden="true"
                >
                  <path d="M12 20.3 4.6 13a4.8 4.8 0 0 1 6.8-6.8l.6.6.6-.6a4.8 4.8 0 0 1 6.8 6.8Z" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              )}
            </button>
          </div>

          {addItem.isError && (
            <div className="mt-3">
              <Alert tone="error">{(addItem.error as Error).message}</Alert>
            </div>
          )}

          <dl className="mt-6 space-y-1.5 border-t border-ink-100 pt-5 text-sm">
            {selectedVariant && (
              <div className="flex gap-2">
                <dt className="text-ink-400">SKU</dt>
                <dd className="font-mono text-xs text-ink-600">{selectedVariant.sku}</dd>
              </div>
            )}
            {product.categoryName && (
              <div className="flex gap-2">
                <dt className="text-ink-400">Category</dt>
                <dd>
                  <Link to={`/category/${product.categorySlug}`} className="text-saffron-600 hover:text-saffron-700">
                    {product.categoryName}
                  </Link>
                </dd>
              </div>
            )}
          </dl>
        </div>
      </div>

      {/* ---- Description ---- */}
      {product.description && (
        <section className="mt-14 max-w-3xl">
          <h2 className="text-lg font-bold text-ink-900">Product details</h2>
          {/*
            Server-sanitised HTML. The API sanitises on write rather than trusting the client to
            sanitise on read, so this renders what was stored.
          */}
          <div
            className="prose-sm mt-3 space-y-3 leading-relaxed text-ink-600 [&_p]:mb-3"
            dangerouslySetInnerHTML={{ __html: product.description }}
          />
        </section>
      )}

      {/* ---- Reviews ---- */}
      <section className="mt-14">
        <h2 className="text-lg font-bold text-ink-900">Customer reviews</h2>

        {reviews && reviews.summary.total > 0 ? (
          <div className="mt-4 grid gap-8 lg:grid-cols-[18rem_1fr]">
            <div className="card-surface p-5">
              <div className="text-center">
                <p className="text-4xl font-bold text-ink-900">{reviews.summary.average.toFixed(1)}</p>
                <div className="mt-1 flex justify-center">
                  <Rating value={reviews.summary.average} size="md" />
                </div>
                <p className="mt-1 text-xs text-ink-400">{reviews.summary.total} reviews</p>
              </div>

              <div className="mt-5 space-y-1.5">
                {([5, 4, 3, 2, 1] as const).map((stars) => {
                  const count = [
                    reviews.summary.fiveStar,
                    reviews.summary.fourStar,
                    reviews.summary.threeStar,
                    reviews.summary.twoStar,
                    reviews.summary.oneStar,
                  ][5 - stars]

                  const percent = reviews.summary.total > 0 ? (count / reviews.summary.total) * 100 : 0

                  return (
                    <div key={stars} className="flex items-center gap-2 text-xs">
                      <span className="w-3 text-ink-500">{stars}</span>
                      <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-ink-100">
                        {/* Width is set once from data, not animated — animating width reflows. */}
                        <div className="h-full rounded-full bg-saffron-400" style={{ width: `${percent}%` }} />
                      </div>
                      <span className="w-7 text-right text-ink-400">{count}</span>
                    </div>
                  )
                })}
              </div>
            </div>

            <ul className="space-y-4">
              {reviews.reviews.items.map((review) => (
                <li key={review.id} className="card-surface p-5">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-semibold text-ink-800">{review.customerName}</p>
                      <div className="mt-1 flex items-center gap-2">
                        <Rating value={review.rating} />
                        {review.isVerifiedPurchase && (
                          <Badge tone="success">Verified purchase</Badge>
                        )}
                      </div>
                    </div>
                    <time className="shrink-0 text-xs text-ink-400" dateTime={review.createdAt}>
                      {formatRelative(review.createdAt)}
                    </time>
                  </div>

                  {review.title && <p className="mt-3 font-medium text-ink-800">{review.title}</p>}
                  <p className="mt-1.5 text-sm leading-relaxed text-ink-600">{review.body}</p>

                  {review.adminReply && (
                    <div className="mt-3 rounded-lg border-l-2 border-saffron-400 bg-saffron-50/60 p-3">
                      <p className="text-xs font-semibold text-saffron-800">Reply from the shop</p>
                      <p className="mt-1 text-sm text-ink-600">{review.adminReply}</p>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <p className="mt-3 text-sm text-ink-500">
            No reviews yet.{' '}
            {reviews?.canReview
              ? 'Be the first to review this product.'
              : (reviews?.cannotReviewReason ?? '')}
          </p>
        )}
      </section>

      {/* ---- Related ---- */}
      {related && related.length > 0 && (
        <section className="mt-14">
          <h2 className="text-lg font-bold text-ink-900">You might also like</h2>
          <div className="mt-5 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
            {related.map((item) => (
              <ProductCard key={item.id} product={item} isSaved={savedIds.has(item.id)} />
            ))}
          </div>
        </section>
      )}
    </div>
  )
}

/** Mirrors the real page's layout so the content swap causes no shift. */
function ProductSkeleton() {
  return (
    <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8" aria-busy="true">
      <div className="skeleton h-3 w-48" />
      <div className="mt-6 grid gap-8 lg:grid-cols-2 lg:gap-12">
        <div className="skeleton aspect-square w-full" />
        <div className="space-y-4">
          <div className="skeleton h-3 w-24" />
          <div className="skeleton h-8 w-4/5" />
          <div className="skeleton h-4 w-32" />
          <div className="skeleton h-4 w-full" />
          <div className="skeleton h-10 w-40" />
          <div className="skeleton h-12 w-full" />
        </div>
      </div>
    </div>
  )
}
