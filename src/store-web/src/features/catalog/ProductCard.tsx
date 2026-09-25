import { memo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Image } from '../../ui/Image'
import { Badge, Rating, Spinner } from '../../ui/primitives'
import { formatPrice, formatPriceRange } from '../../lib/format'
import { useCartMutations } from '../cart/useCart'
import { useWishlistToggle } from '../wishlist/useWishlist'
import type { ProductCard as ProductCardModel } from '../../lib/types'

interface Props {
  product: ProductCardModel
  /** Set true only for the first row above the fold, so the LCP image is not lazy-loaded. */
  priority?: boolean
  isSaved?: boolean
}

/**
 * The catalogue grid card.
 *
 * `memo`'d because a filter change re-renders the grid, and re-rendering 24 cards whose props are
 * unchanged is wasted main-thread work that shows up directly in INP.
 */
export const ProductCard = memo(function ProductCard({ product, priority = false, isSaved = false }: Props) {
  const { addItem } = useCartMutations()
  const toggleWishlist = useWishlistToggle()
  const [justAdded, setJustAdded] = useState(false)

  const isSingleVariant = product.variantCount <= 1
  const discount = product.discountPercent

  async function handleQuickAdd(event: React.MouseEvent) {
    // The card is a link; quick-add must not navigate.
    event.preventDefault()
    event.stopPropagation()

    // A product with several pack sizes has no single correct thing to add, so the shopper is
    // sent to the detail page to choose rather than having one silently picked for them.
    if (!isSingleVariant || !product.inStock) {
      return
    }

    try {
      await addItem.mutateAsync({ productVariantId: await resolveDefaultVariantId(product.slug), quantity: 1 })
      setJustAdded(true)
      window.setTimeout(() => setJustAdded(false), 1600)
    } catch {
      // The cart hook surfaces the error; the card stays in its previous state.
    }
  }

  return (
    // h-full so the card fills its grid cell — in a stretched rail that makes every card in the
    // row exactly the same height regardless of how its text wraps.
    <article className="group card-surface lift-on-hover relative flex h-full flex-col overflow-hidden">
      {/* Wishlist sits outside the link so it does not navigate. */}
      <button
        type="button"
        onClick={(event) => {
          event.preventDefault()
          toggleWishlist.mutate(product.id)
        }}
        aria-label={isSaved ? `Remove ${product.name} from saved items` : `Save ${product.name}`}
        aria-pressed={isSaved}
        className="absolute right-2 top-2 z-10 grid h-9 w-9 place-items-center rounded-full bg-paper-raised/90 text-ink-400 shadow-sm backdrop-blur transition-colors hover:text-chilli-500 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-saffron-500"
      >
        <svg
          className={`h-4.5 w-4.5 transition-transform duration-200 ${isSaved ? 'scale-110 text-chilli-500' : ''}`}
          viewBox="0 0 24 24"
          fill={isSaved ? 'currentColor' : 'none'}
          stroke="currentColor"
          strokeWidth="1.8"
          aria-hidden="true"
        >
          <path
            d="M12 20.3 4.6 13a4.8 4.8 0 0 1 6.8-6.8l.6.6.6-.6a4.8 4.8 0 0 1 6.8 6.8Z"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      </button>

      <Link to={`/product/${product.slug}`} className="flex h-full flex-1 flex-col">
        <div className="relative">
          <Image
            src={product.thumbnailUrl ?? product.imageUrl}
            alt={product.name}
            // Falls back to a square when the API has no dimensions, which still reserves a
            // correct-shaped box rather than collapsing to zero height.
            width={product.imageWidth || 400}
            height={product.imageHeight || 400}
            blurHash={product.blurHash}
            priority={priority}
            sizes="(min-width: 1024px) 22vw, (min-width: 640px) 30vw, 45vw"
            className="w-full"
          />

          <div className="absolute left-2 top-2 flex flex-col items-start gap-1">
            {discount !== undefined && discount > 0 && <Badge tone="sale">−{discount}%</Badge>}
            {product.badge === 'New' && <Badge tone="new">New</Badge>}
          </div>

          {!product.inStock && (
            <div className="absolute inset-0 grid place-items-center bg-paper/70 backdrop-blur-[1px]">
              <span className="rounded-full bg-ink-800 px-3 py-1 text-xs font-semibold text-paper">
                Out of stock
              </span>
            </div>
          )}
        </div>

        {/*
          Every slot below has a reserved height, including the optional ones.

          A card with a brand and a rating is otherwise taller than one without, and — more
          importantly — taller than the skeleton it replaces. In a grid, one differing card
          changes its whole row's height, so the content swap becomes a layout shift. Rendering
          the brand and rating slots unconditionally (empty when absent) makes every card, and its
          skeleton, exactly the same size.
        */}
        <div className="flex flex-1 flex-col gap-1.5 p-3">
          <span className="h-4 truncate text-[11px] font-medium uppercase tracking-wide text-ink-400">
            {product.brandName ?? ''}
          </span>

          {/* Fixed height, so a one-line and a two-line name produce identically sized cards. */}
          <h3 className="line-clamp-2-fixed text-sm font-medium leading-snug text-ink-800">
            {product.name}
          </h3>

          <span className="flex h-4 items-center">
            {product.ratingCount > 0 && (
              <Rating value={product.ratingAverage} count={product.ratingCount} />
            )}
          </span>

          {/*
            Fixed height on the price block.

            A range like "HK$42 – HK$193.20" wraps to two lines while "HK$97.70" does not, and a
            struck-through original adds a third. Left to size itself, this one row made every
            card in a rail a different height — which is exactly the ragged, collapsed look the
            grid had. `h-11` fits the worst case; `leading-tight` keeps two lines inside it.
          */}
          <div className="mt-auto flex h-11 items-end justify-between gap-2 pt-1">
            <div className="flex min-w-0 flex-col justify-end">
              <span className="text-[0.95rem] font-semibold leading-tight text-ink-900">
                {product.hasPriceRange
                  ? formatPriceRange(product.minPrice, product.maxPrice)
                  : formatPrice(product.minPrice)}
              </span>

              {product.compareAtPrice !== undefined && product.compareAtPrice > product.minPrice && (
                <span className="text-[11px] leading-tight text-ink-400 line-through">
                  {formatPrice(product.compareAtPrice)}
                </span>
              )}
            </div>

            {product.inStock && (
              <button
                type="button"
                onClick={handleQuickAdd}
                disabled={addItem.isPending}
                aria-label={isSingleVariant ? `Add ${product.name} to cart` : `Choose options for ${product.name}`}
                className={`grid h-9 w-9 shrink-0 place-items-center rounded-lg text-white transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-saffron-500 ${
                  justAdded ? 'animate-pop bg-cardamom-500' : 'bg-saffron-500 hover:bg-saffron-600'
                }`}
              >
                {addItem.isPending ? (
                  <Spinner />
                ) : justAdded ? (
                  <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true">
                    <path d="m5 13 4 4L19 7" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                ) : (
                  <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                    <path d="M12 5v14M5 12h14" strokeLinecap="round" />
                  </svg>
                )}
              </button>
            )}
          </div>

          {/* Reserved the same way — an "N sizes" line must not make this card taller than its
              single-variant neighbours. */}
          <span className="h-4 text-[11px] text-ink-400">
            {product.variantCount > 1 ? `${product.variantCount} sizes available` : ''}
          </span>
        </div>
      </Link>
    </article>
  )
})

/**
 * Looks up a product's default variant id for quick-add.
 *
 * The card payload deliberately omits variant ids — carrying them for 24 products would bloat the
 * grid response for a feature most cards never use. Fetching the one product on demand keeps the
 * listing lean, and the detail response is already cached by the time the shopper clicks through.
 */
async function resolveDefaultVariantId(slug: string): Promise<string> {
  const { api } = await import('../../lib/api')
  const detail = await api.get<{ variants: { id: string; isDefault: boolean }[] }>(
    `/catalog/products/${slug}`,
  )

  return (detail.variants.find((variant) => variant.isDefault) ?? detail.variants[0]).id
}

/**
 * Grid skeleton.
 *
 * Mirrors the real card's slots one-for-one — same image ratio, same gaps, same reserved heights
 * for brand, name, rating, price and the variant line. That exact correspondence is the whole
 * point: if the skeleton is even a few pixels shorter, every row jumps when the data lands.
 */
export function ProductCardSkeleton() {
  return (
    <div className="card-surface overflow-hidden">
      <div className="skeleton aspect-square w-full rounded-none" />
      <div className="flex flex-col gap-1.5 p-3">
        <div className="skeleton h-4 w-1/3" />
        {/* Matches .line-clamp-2-fixed's min-height on the real card's name. */}
        <div className="skeleton h-[2.75rem] w-full" />
        <div className="skeleton h-4 w-2/3" />
        <div className="flex items-end justify-between gap-2 pt-1">
          <div className="skeleton h-9 w-1/2" />
          <div className="skeleton h-9 w-9 shrink-0" />
        </div>
        <div className="skeleton h-4 w-1/2" />
      </div>
    </div>
  )
}
