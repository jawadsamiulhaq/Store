import { Link } from 'react-router-dom'
import { ProductCard, ProductCardSkeleton } from '../../features/catalog/ProductCard'
import { useFeaturedProducts, useNewArrivals, useOnSaleProducts } from '../../features/catalog/useCatalog'
import { useWishlistIds } from '../../features/wishlist/useWishlist'
import { useStore } from '../../app/providers/StoreProvider'
import { ButtonLink } from '../../ui/primitives'
import { formatPrice } from '../../lib/format'
import { useReveal } from '../../lib/useReveal'
import type { ProductCard as ProductCardModel } from '../../lib/types'

/**
 * Aisle glyphs — grain, leaf, flame, bottle, snowflake, tin, bowl, droplet, cup, box, basket, jar.
 *
 * Twelve distinct paths assigned by position. Suggestive rather than literal: the category order
 * is set by the client in admin, so a fixed name→icon map would break the moment they rename or
 * reorder an aisle.
 */
const AISLE_GLYPHS = [
  'M12 3c3 3 4 6 4 9a4 4 0 0 1-8 0c0-3 1-6 4-9ZM12 12v9',
  'M11 21c0-6 3-11 9-13-1 7-4 11-9 13ZM11 21c0-5-2-9-7-10 .5 5 2.8 8.4 7 10Z',
  'M12 3c1 4 5 5 5 9a5 5 0 0 1-10 0c0-2 1-3 2-4 .3 2 1 3 3 3-1-3 0-6 0-8Z',
  'M9 3h6v3l2 3v10a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2V9l2-3ZM7 12h10',
  'M12 2v20M4 7l16 10M20 7 4 17',
  'M5 8h14v11a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2ZM5 8l1.5-4h11L19 8',
  'M4 11h16a8 8 0 0 1-16 0ZM10 4c0 1-1 1.5-1 2.5M14 4c0 1-1 1.5-1 2.5M3 20h18',
  'M12 3c3.5 4.5 5.5 7.4 5.5 10a5.5 5.5 0 0 1-11 0C6.5 10.4 8.5 7.5 12 3Z',
  'M5 8h11v6a5 5 0 0 1-10 0ZM16 9h2a2.5 2.5 0 0 1 0 5h-2M4 21h14',
  'M3 8l9-4 9 4-9 4-9-4ZM3 8v8l9 4 9-4V8',
  'M4 8h16l-1.3 11a2 2 0 0 1-2 1.8H7.3a2 2 0 0 1-2-1.8ZM9 8V6a3 3 0 0 1 6 0v2',
  'M8 3h8v2l-1 2v3l2 4v5a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2v-5l2-4V7L8 5Z',
] as const

/**
 * Home page.
 *
 * Built as a **bento grid**, not the conventional full-width hero slab followed by uniform 4-up
 * product rows. That template is what every grocery site uses — including the legacy store — and
 * it is precisely why the previous pass read as generic.
 *
 * Here the first screen is a composition of mixed-size tiles (headline, delivery promise, two
 * aisles, a live offer count), and the product sections below are horizontal rails with a
 * deliberate "peek" at the next card rather than grid rows that stop dead at the container edge.
 */
export default function HomePage() {
  const { categories, setting, settingNumber } = useStore()
  const { ids: savedIds } = useWishlistIds()

  const featured = useFeaturedProducts()
  const newArrivals = useNewArrivals()
  const onSale = useOnSaleProducts()

  // Only the editorial block needs one here — the rails own theirs, and the bento and aisle grid
  // are above the fold on every size, where a scroll-triggered reveal would never fire.
  const editorial = useReveal<HTMLElement>()

  const freeOver = settingNumber('checkout.free-shipping-threshold', 300)
  const offerCount = onSale.data?.products.totalCount ?? 0

  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
      {/* ================= BENTO ================= */}
      <section className="grid gap-3 pt-6 sm:gap-4 lg:grid-cols-12 lg:grid-rows-2">
        {/* Headline tile — spans two rows on desktop so the composition has a clear anchor. */}
        <div className="bento bento-wash-indigo flex flex-col justify-between p-7 sm:p-9 lg:col-span-7 lg:row-span-2">
          <div>
            <span className="inline-flex items-center gap-2 rounded-full bg-saffron-500/10 px-3 py-1 text-xs font-semibold text-saffron-700">
              <span className="h-1.5 w-1.5 rounded-full bg-saffron-500" aria-hidden="true" />
              Delivering across Hong Kong
            </span>

            <h1 className="mt-5 text-[2.25rem] font-bold leading-[1.04] tracking-tight text-ink-900 sm:text-5xl lg:text-[3.4rem]">
              Everything your
              <span className="block text-saffron-600">kitchen runs on.</span>
            </h1>

            <p className="mt-4 max-w-md text-base leading-relaxed text-ink-500 sm:text-lg">
              {setting(
                'store.tagline',
                'Rice, spices, lentils, cooking oil and frozen favourites — sourced properly, delivered to your door.',
              )}
            </p>
          </div>

          <div className="mt-8 flex flex-wrap items-center gap-3">
            <ButtonLink to="/shop" size="lg">
              Start shopping
            </ButtonLink>
            <Link
              to="/category/spices-and-masala"
              className="inline-flex h-12 items-center rounded-xl border border-ink-200 bg-paper-raised px-6 text-sm font-semibold text-ink-700 transition-colors hover:border-saffron-300 hover:text-saffron-700"
            >
              Browse spices
            </Link>
          </div>
        </div>

        {/* Delivery promise — the single most-asked question, answered on the first screen. */}
        <div className="bento bg-saffron-600 p-6 text-white lg:col-span-5">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-3xl font-bold tracking-tight sm:text-4xl">{formatPrice(freeOver)}</p>
              <p className="mt-1 text-sm font-medium text-saffron-50">Free delivery over this, in Kowloon</p>
              <p className="mt-3 text-xs leading-relaxed text-saffron-100/90">
                Order before 14:00 for same-day. Or collect in store at Ngau Chi Wan Market.
              </p>
            </div>

            <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-white/15">
              <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M3 7h11v9H3zM14 10h4l3 3v3h-7z" />
                <circle cx="7" cy="18" r="1.6" />
                <circle cx="17.5" cy="18" r="1.6" />
              </svg>
            </span>
          </div>
        </div>

        {/*
          Second row, 3 + 2 of the five columns beneath the delivery tile.

          An earlier split was 2 + 2 + 1, which squeezed the offer tile into roughly 95px — the
          number and its label had nowhere to sit and the caption wrapped mid-phrase. Two tiles at
          a readable width beat three cramped ones.
        */}
        {(() => {
          const aisle = categories[0] ?? {
            id: 'placeholder',
            name: 'Rice & Grains',
            slug: 'rice-and-grains',
            productCount: 0,
          }

          return (
            <Link
              to={`/category/${aisle.slug}`}
              className="bento bento-wash-brass group flex items-end justify-between gap-4 p-5 transition-colors hover:border-cardamom-300 lg:col-span-3"
            >
              <span>
                <span className="block text-base font-semibold leading-tight text-ink-800">
                  {aisle.name}
                </span>
                <span className="block text-xs text-ink-500">
                  {aisle.productCount > 0 ? `${aisle.productCount} items` : 'Browse the aisle'}
                </span>
              </span>

              <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-white/80 text-cardamom-600 transition-transform duration-200 group-hover:scale-110">
                <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <path d={AISLE_GLYPHS[0]} />
                </svg>
              </span>
            </Link>
          )
        })()}

        <Link
          to="/shop?onSale=true"
          className="bento group flex flex-col justify-between bg-ink-900 p-5 text-white transition-colors hover:bg-ink-800 lg:col-span-2"
        >
          <span className="text-xs font-semibold uppercase tracking-wide text-ink-300">On offer</span>
          <span className="flex items-end justify-between gap-2">
            <span>
              {/* Fixed height, so the tile does not jump when the count arrives. */}
              <span className="block h-10 text-4xl font-bold leading-none tracking-tight">
                {onSale.isLoading ? '' : offerCount}
              </span>
              <span className="block text-xs text-ink-300">reduced lines</span>
            </span>
            <span className="text-xs font-semibold text-saffron-300 transition-transform duration-200 group-hover:translate-x-0.5">
              See all →
            </span>
          </span>
        </Link>
      </section>

      {/* ================= AISLES ================= */}
      <section className="pt-14">
        <SectionHeading title="Shop by aisle" href="/shop" linkLabel="Browse everything" />

        <div className="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-6">
          {categories.length === 0
            ? Array.from({ length: 12 }, (_, index) => (
                <div key={index} className="skeleton h-[6.75rem] w-full rounded-3xl" />
              ))
            : categories.slice(0, 12).map((category, index) => (
                <Link
                  key={category.id}
                  to={`/category/${category.slug}`}
                  className="card-surface group flex h-[6.75rem] flex-col items-center justify-center gap-2 p-3 text-center transition-all duration-200 hover:-translate-y-0.5 hover:border-saffron-200"
                >
                  <span
                    className="grid h-10 w-10 place-items-center rounded-xl transition-transform duration-200 group-hover:scale-110"
                    style={{
                      // Alternates green and apricot so the row has rhythm rather than
                      // twelve identical swatches.
                      backgroundColor: index % 3 === 1 ? 'var(--color-cardamom-50)' : 'var(--color-saffron-50)',
                      color: index % 3 === 1 ? 'var(--color-cardamom-600)' : 'var(--color-saffron-600)',
                    }}
                  >
                    <svg className="h-[21px] w-[21px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                      <path d={AISLE_GLYPHS[index % AISLE_GLYPHS.length]} />
                    </svg>
                  </span>
                  <span className="line-clamp-2 text-xs font-medium leading-tight text-ink-700">
                    {category.name}
                  </span>
                </Link>
              ))}
        </div>
      </section>

      {/* ================= RAILS ================= */}
      <ProductRail
        title="Best sellers"
        subtitle="What our regulars keep coming back for"
        href="/shop?sort=BestSelling"
        products={featured.data?.products.items}
        isLoading={featured.isLoading}
        savedIds={savedIds}
        priority
      />

      <ProductRail
        title="On offer this week"
        subtitle="Reduced while stocks last"
        href="/shop?onSale=true"
        products={onSale.data?.products.items}
        isLoading={onSale.isLoading}
        savedIds={savedIds}
      />

      {/* Editorial break — stops the page reading as an unbroken run of product rows. */}
      <section ref={editorial.ref} {...editorial.revealProps} className="pt-14">
        <div className="bento bento-wash-indigo grid items-center gap-6 p-7 sm:p-10 lg:grid-cols-2">
          <div>
            <h2 className="text-2xl font-bold tracking-tight text-ink-900 sm:text-3xl">
              A proper provision store, online.
            </h2>
            <p className="mt-3 max-w-md leading-relaxed text-ink-500">
              We have run the stall at Ngau Chi Wan Market for years. Same shelves, same buyers,
              same prices — now with delivery across Kowloon, Hong Kong Island and the New
              Territories.
            </p>
            <Link
              to="/page/about"
              className="mt-5 inline-flex text-sm font-semibold text-saffron-700 hover:text-saffron-800"
            >
              About the shop →
            </Link>
          </div>

          <dl className="grid grid-cols-3 gap-4">
            {[
              { value: '4,000+', label: 'pantry lines' },
              { value: 'Same day', label: 'in Kowloon' },
              { value: '3', label: 'delivery zones' },
            ].map((stat) => (
              <div key={stat.label} className="rounded-2xl bg-white/70 p-4">
                <dt className="text-xl font-bold tracking-tight text-ink-900">{stat.value}</dt>
                <dd className="mt-0.5 text-xs text-ink-500">{stat.label}</dd>
              </div>
            ))}
          </dl>
        </div>
      </section>

      <ProductRail
        title="New in store"
        subtitle="Just landed on the shelves"
        href="/shop?sort=Newest"
        products={newArrivals.data?.products.items}
        isLoading={newArrivals.isLoading}
        savedIds={savedIds}
      />

      <div className="pb-4" />
    </div>
  )
}

function SectionHeading({
  title,
  subtitle,
  href,
  linkLabel = 'View all',
}: {
  title: string
  subtitle?: string
  href: string
  linkLabel?: string
}) {
  return (
    <div className="flex items-end justify-between gap-4">
      <div>
        <h2 className="text-xl font-bold tracking-tight text-ink-900 sm:text-2xl">{title}</h2>
        {subtitle && <p className="mt-1 text-sm text-ink-500">{subtitle}</p>}
      </div>
      <Link
        to={href}
        className="shrink-0 rounded-lg px-3 py-1.5 text-sm font-semibold text-saffron-700 transition-colors hover:bg-saffron-50"
      >
        {linkLabel} →
      </Link>
    </div>
  )
}

/**
 * Horizontal rail with peek.
 *
 * A grid row of exactly four cards ends flush with the container and looks like the whole set.
 * A scroller whose fifth card is visibly cut off communicates "there is more" without a word of
 * copy — and on mobile it becomes a natural swipe instead of a two-wide grid.
 */
function ProductRail({
  title,
  subtitle,
  href,
  products,
  isLoading,
  savedIds,
  priority = false,
}: {
  title: string
  subtitle?: string
  href: string
  products?: ProductCardModel[]
  isLoading: boolean
  savedIds: Set<string>
  priority?: boolean
}) {
  // Called before the early return would be a conditional hook, so the reveal is set up first.
  const { ref, revealProps } = useReveal<HTMLElement>()

  if (!isLoading && (!products || products.length === 0)) {
    return null
  }

  return (
    <section ref={ref} {...revealProps} className="pt-14">
      <SectionHeading title={title} subtitle={subtitle} href={href} />

      {/*
        Negative margin plus matching padding lets the rail bleed to the screen edge on mobile,
        so the peeking card is not clipped by the page gutter.
      */}
      <div className="-mx-4 mt-5 px-4 sm:-mx-6 sm:px-6 lg:mx-0 lg:px-0">
        <div className="rail no-scrollbar">
          {isLoading
            ? Array.from({ length: 6 }, (_, index) => (
                // No width class: the rail's grid track sets it, so the skeleton cannot drift out
                // of step with the real card.
                <div key={index}>
                  <ProductCardSkeleton />
                </div>
              ))
            : products?.map((product, index) => (
                // h-full passes the stretched row height down to the card itself.
                <div key={product.id} className="h-full">
                  <ProductCard
                    product={product}
                    priority={priority && index < 4}
                    isSaved={savedIds.has(product.id)}
                    index={index}
                  />
                </div>
              ))}
        </div>
      </div>
    </section>
  )
}
