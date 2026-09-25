import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { formatPrice } from '../../lib/format'
import { useAuth } from '../providers/AuthProvider'
import { useStore } from '../providers/StoreProvider'
import { useCartCount } from '../../features/cart/useCart'
import { Image } from '../../ui/Image'
import type { SearchSuggestion } from '../../lib/types'

export function SiteHeader() {
  // `categories` is read by CategoryBar and MobileDrawer from their own useStore() call, so it
  // is not destructured here.
  const { setting } = useStore()
  const { isAuthenticated, user, logout } = useAuth()
  const cartCount = useCartCount()
  const [menuOpen, setMenuOpen] = useState(false)

  return (
    <header className="sticky top-0 z-40 border-b border-ink-100 bg-paper/85 backdrop-blur-md">
      {/*
        Delivery strip, in brand green rather than near-black.

        The dark bar belonged to the previous dark-hero design; against a bright, airy page it
        read as a heavy black band pinned to the top. Green keeps the same information at the same
        prominence without weighing the page down.
      */}
      <div className="hidden bg-saffron-600 text-white sm:block">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-4 py-1.5 text-xs sm:px-6 lg:px-8">
          <span className="font-medium">
            Free delivery over {formatPrice(Number(setting('checkout.free-shipping-threshold', '300')))} in Kowloon
          </span>
          <span className="text-saffron-100">{setting('store.opening-hours', 'Mon–Sun, 09:00–21:00')}</span>
        </div>
      </div>

      <div className="mx-auto flex h-16 max-w-7xl items-center gap-3 px-4 sm:px-6 lg:gap-6 lg:px-8">
        <button
          type="button"
          onClick={() => setMenuOpen(true)}
          aria-label="Open menu"
          className="-ml-2 grid h-10 w-10 place-items-center rounded-lg text-ink-600 lg:hidden"
        >
          <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <path d="M4 6h16M4 12h16M4 18h16" strokeLinecap="round" />
          </svg>
        </button>

        <Link to="/" className="flex shrink-0 items-center gap-2.5">
          {/* Inline SVG mark — no image request, and it scales crisply. */}
          <span className="grid h-9 w-9 place-items-center rounded-xl bg-saffron-500 text-white shadow-sm">
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" aria-hidden="true">
              <path d="M4 8h16l-1.3 11a2 2 0 0 1-2 1.8H7.3a2 2 0 0 1-2-1.8Z" strokeLinejoin="round" />
              <path d="M9 8V6a3 3 0 0 1 6 0v2" strokeLinecap="round" />
            </svg>
          </span>
          <span className="hidden flex-col leading-none sm:flex">
            <span className="font-display text-[15px] font-bold tracking-tight text-ink-900">
              {setting('store.name', 'Waqas Provision Store')}
            </span>
            <span className="text-[10px] uppercase tracking-[0.14em] text-ink-400">Hong Kong</span>
          </span>
        </Link>

        <SearchBox />

        <nav className="ml-auto flex items-center gap-1">
          <Link
            to={isAuthenticated ? '/account/wishlist' : '/login'}
            aria-label="Saved items"
            className="grid h-10 w-10 place-items-center rounded-lg text-ink-600 transition-colors hover:bg-ink-100"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
              <path d="M12 20.3 4.6 13a4.8 4.8 0 0 1 6.8-6.8l.6.6.6-.6a4.8 4.8 0 0 1 6.8 6.8Z" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </Link>

          <Link
            to="/cart"
            aria-label={`Cart, ${cartCount} item${cartCount === 1 ? '' : 's'}`}
            className="relative grid h-10 w-10 place-items-center rounded-lg text-ink-600 transition-colors hover:bg-ink-100"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
              <path d="M3 4h2l2.6 12.4a2 2 0 0 0 2 1.6h8.2a2 2 0 0 0 2-1.6L21 8H6" strokeLinecap="round" strokeLinejoin="round" />
              <circle cx="9.5" cy="20" r="1.4" fill="currentColor" stroke="none" />
              <circle cx="17.5" cy="20" r="1.4" fill="currentColor" stroke="none" />
            </svg>

            {cartCount > 0 && (
              // key forces a remount so the pop animation replays on every change.
              <span
                key={cartCount}
                className="animate-pop absolute -right-0.5 -top-0.5 grid h-5 min-w-5 place-items-center rounded-full bg-chilli-500 px-1 text-[10px] font-bold text-white"
              >
                {cartCount > 99 ? '99+' : cartCount}
              </span>
            )}
          </Link>

          <AccountMenu isAuthenticated={isAuthenticated} userName={user?.firstName} isStaff={user?.isStaff ?? false} onLogout={logout} />
        </nav>
      </div>

      <CategoryBar />

      {menuOpen && <MobileDrawer onClose={() => setMenuOpen(false)} />}
    </header>
  )
}

/**
 * Desktop category bar, with a mega-menu.
 *
 * The previous version was a flat row of links in a horizontally-scrolling container — which
 * both looked unfinished (a grey scrollbar sat permanently under the navigation) and wasted the
 * category tree the API already returns: subcategories were only reachable after landing on a
 * parent page.
 *
 * This version keeps a fixed height (the CLS reservation still matters), hides the scrollbar,
 * and opens a panel of subcategories on hover or focus. Opening is delayed slightly and closing
 * more so, because a menu that springs open as the pointer crosses it — or snaps shut in the gap
 * between the trigger and the panel — is worse than no menu at all.
 */
function CategoryBar() {
  const { categories } = useStore()
  const [openId, setOpenId] = useState<string | null>(null)
  const timer = useRef<number | undefined>(undefined)

  function open(id: string) {
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => setOpenId(id), 90)
  }

  function close() {
    window.clearTimeout(timer.current)
    // Generous enough to cross the gap between the trigger and the panel below it.
    timer.current = window.setTimeout(() => setOpenId(null), 180)
  }

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const active = categories.find((category) => category.id === openId)

  return (
    <nav
      className="relative hidden border-t border-ink-100 lg:block"
      onMouseLeave={close}
      aria-label="Product categories"
    >
      <div className="no-scrollbar mx-auto flex h-12 max-w-7xl items-stretch gap-0.5 overflow-x-auto px-4 sm:px-6 lg:px-8">
        <Link
          to="/shop"
          className="flex shrink-0 items-center whitespace-nowrap rounded-lg px-3 text-sm font-semibold text-ink-800 transition-colors hover:bg-ink-50"
        >
          All products
        </Link>

        {/*
          Six, not eight. Each item gained a chevron for its mega-menu, which widened the row
          enough to push the "Offers" link off the right edge at 1440px. Six leaves room for it
          and for the full aisle grid to do the rest of the work further down the page.
        */}
        {categories.slice(0, 6).map((category) => {
          const isOpen = openId === category.id

          return (
            <div key={category.id} className="flex shrink-0 items-stretch">
              <Link
                to={`/category/${category.slug}`}
                onMouseEnter={() => open(category.id)}
                onFocus={() => setOpenId(category.id)}
                aria-expanded={category.children.length > 0 ? isOpen : undefined}
                className={`flex items-center gap-1 whitespace-nowrap rounded-lg px-3 text-sm transition-colors ${
                  isOpen ? 'bg-saffron-50 text-saffron-700' : 'text-ink-600 hover:bg-ink-50 hover:text-ink-900'
                }`}
              >
                {category.name}
                {category.children.length > 0 && (
                  <svg
                    className={`h-3 w-3 transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`}
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.4"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    aria-hidden="true"
                  >
                    <path d="m6 9 6 6 6-6" />
                  </svg>
                )}
              </Link>
            </div>
          )
        })}

        {/* Deals gets its own treatment — it is a merchandising entry point, not a category. */}
        <Link
          to="/shop?onSale=true"
          className="ml-auto flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-lg px-3 text-sm font-semibold text-chilli-600 transition-colors hover:bg-chilli-50"
        >
          <span className="h-1.5 w-1.5 rounded-full bg-chilli-500" aria-hidden="true" />
          Offers
        </Link>
      </div>

      {/* Mega panel. Absolutely positioned so opening it never moves the page. */}
      {active && active.children.length > 0 && (
        <div
          className="animate-fade-in absolute inset-x-0 top-full z-50 border-b border-ink-100 bg-paper-raised shadow-raised"
          onMouseEnter={() => window.clearTimeout(timer.current)}
          onMouseLeave={close}
        >
          <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
            <div className="flex items-baseline justify-between">
              <h2 className="text-sm font-bold text-ink-900">{active.name}</h2>
              <Link
                to={`/category/${active.slug}`}
                className="text-xs font-semibold text-saffron-700 hover:text-saffron-800"
              >
                Shop all {active.productCount > 0 ? `(${active.productCount})` : ''} →
              </Link>
            </div>

            <ul className="mt-4 grid grid-cols-4 gap-x-6 gap-y-1">
              {active.children.map((child) => (
                <li key={child.id}>
                  <Link
                    to={`/category/${child.slug}`}
                    className="flex items-center justify-between rounded-lg px-2 py-1.5 text-sm text-ink-600 transition-colors hover:bg-saffron-50 hover:text-saffron-700"
                  >
                    <span className="truncate">{child.name}</span>
                    <span className="ml-2 shrink-0 text-xs text-ink-300">{child.productCount}</span>
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        </div>
      )}
    </nav>
  )
}

/**
 * Search with autocomplete.
 *
 * Debounced at 220 ms. Firing a request per keystroke would put ~8 requests in flight for the word
 * "basmati", most of them already irrelevant by the time they return — and would make the
 * suggestion list flicker as out-of-order responses landed.
 */
function SearchBox() {
  const navigate = useNavigate()
  const [term, setTerm] = useState('')
  const [debounced, setDebounced] = useState('')
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(term), 220)
    return () => window.clearTimeout(timer)
  }, [term])

  // Close on outside click.
  useEffect(() => {
    function handlePointerDown(event: PointerEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    document.addEventListener('pointerdown', handlePointerDown)
    return () => document.removeEventListener('pointerdown', handlePointerDown)
  }, [])

  const { data: suggestions } = useQuery({
    queryKey: ['suggest', debounced],
    queryFn: () => api.get<SearchSuggestion[]>(`/catalog/suggest${qs({ q: debounced, take: 6 })}`),
    // Two characters is the server's own minimum; asking below that is a guaranteed empty result.
    enabled: debounced.trim().length >= 2,
    staleTime: 5 * 60 * 1000,
  })

  function submit(event: React.FormEvent) {
    event.preventDefault()

    if (term.trim()) {
      setOpen(false)
      navigate(`/search${qs({ search: term.trim() })}`)
    }
  }

  return (
    <div ref={containerRef} className="relative flex-1 lg:max-w-xl">
      <form onSubmit={submit} role="search">
        <label htmlFor="site-search" className="sr-only">
          Search products
        </label>
        <div className="relative">
          <svg
            className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-300"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            aria-hidden="true"
          >
            <circle cx="11" cy="11" r="7" />
            <path d="m20 20-3.5-3.5" strokeLinecap="round" />
          </svg>

          <input
            id="site-search"
            type="search"
            value={term}
            onChange={(event) => {
              setTerm(event.target.value)
              setOpen(true)
            }}
            onFocus={() => setOpen(true)}
            placeholder="Search rice, spices, oil…"
            autoComplete="off"
            className="h-10 w-full rounded-xl border border-ink-200 bg-paper-raised pl-9 pr-3 text-sm text-ink-800 placeholder:text-ink-300 focus:border-saffron-400 focus:outline-2 focus:outline-offset-1 focus:outline-saffron-500"
          />
        </div>
      </form>

      {open && suggestions && suggestions.length > 0 && (
        <ul className="animate-fade-in absolute left-0 right-0 top-full z-50 mt-2 overflow-hidden rounded-xl border border-ink-100 bg-paper-raised shadow-overlay">
          {suggestions.map((suggestion) => (
            <li key={suggestion.slug}>
              <Link
                to={`/product/${suggestion.slug}`}
                onClick={() => {
                  setOpen(false)
                  setTerm('')
                }}
                className="flex items-center gap-3 px-3 py-2.5 transition-colors hover:bg-ink-50"
              >
                <Image
                  src={suggestion.thumbnailUrl}
                  alt=""
                  width={40}
                  height={40}
                  className="h-10 w-10 shrink-0 rounded-lg"
                />
                <span className="flex-1 truncate text-sm text-ink-700">{suggestion.name}</span>
                <span className="text-sm font-semibold text-ink-900">{formatPrice(suggestion.minPrice)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function AccountMenu({
  isAuthenticated,
  userName,
  isStaff,
  onLogout,
}: {
  isAuthenticated: boolean
  userName?: string
  isStaff: boolean
  onLogout: () => Promise<void>
}) {
  const [open, setOpen] = useState(false)

  if (!isAuthenticated) {
    return (
      <Link
        to="/login"
        className="ml-1 rounded-lg bg-ink-800 px-3.5 py-2 text-sm font-medium text-paper transition-colors hover:bg-ink-900"
      >
        Sign in
      </Link>
    )
  }

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        aria-haspopup="menu"
        className="flex h-10 items-center gap-1.5 rounded-lg px-2 text-ink-700 transition-colors hover:bg-ink-100"
      >
        <span className="grid h-7 w-7 place-items-center rounded-full bg-saffron-100 text-xs font-bold text-saffron-700">
          {userName?.[0]?.toUpperCase() ?? 'A'}
        </span>
        <span className="hidden text-sm sm:inline">{userName}</span>
      </button>

      {open && (
        <>
          {/* Backdrop closes the menu on any outside tap, including on touch. */}
          <button
            type="button"
            className="fixed inset-0 z-40 cursor-default"
            aria-hidden="true"
            tabIndex={-1}
            onClick={() => setOpen(false)}
          />

          <div
            role="menu"
            className="animate-fade-rise absolute right-0 top-full z-50 mt-2 w-48 overflow-hidden rounded-xl border border-ink-100 bg-paper-raised py-1 shadow-overlay"
          >
            {[
              { to: '/account', label: 'My account' },
              { to: '/account/orders', label: 'My orders' },
              { to: '/account/addresses', label: 'Addresses' },
              { to: '/account/wishlist', label: 'Saved items' },
            ].map((item) => (
              <Link
                key={item.to}
                to={item.to}
                role="menuitem"
                onClick={() => setOpen(false)}
                className="block px-3.5 py-2 text-sm text-ink-700 transition-colors hover:bg-ink-50"
              >
                {item.label}
              </Link>
            ))}

            {isStaff && (
              <>
                <hr className="my-1 border-ink-100" />
                <Link
                  to="/admin"
                  role="menuitem"
                  onClick={() => setOpen(false)}
                  className="block px-3.5 py-2 text-sm font-medium text-saffron-700 transition-colors hover:bg-saffron-50"
                >
                  Admin area
                </Link>
              </>
            )}

            <hr className="my-1 border-ink-100" />
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setOpen(false)
                void onLogout()
              }}
              className="block w-full px-3.5 py-2 text-left text-sm text-chilli-600 transition-colors hover:bg-chilli-50"
            >
              Sign out
            </button>
          </div>
        </>
      )}
    </div>
  )
}

function MobileDrawer({ onClose }: { onClose: () => void }) {
  const { categories } = useStore()

  // Prevents the page behind the drawer from scrolling.
  useEffect(() => {
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = previous
    }
  }, [])

  // Escape closes, as any dialog should.
  useEffect(() => {
    function handleKey(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', handleKey)
    return () => document.removeEventListener('keydown', handleKey)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-50 lg:hidden">
      <button
        type="button"
        aria-label="Close menu"
        onClick={onClose}
        className="animate-fade-in absolute inset-0 bg-ink-900/40 backdrop-blur-sm"
      />

      <div className="animate-fade-rise absolute inset-y-0 left-0 w-[85%] max-w-sm overflow-y-auto bg-paper-raised shadow-overlay">
        <div className="flex h-16 items-center justify-between border-b border-ink-100 px-4">
          <span className="font-display font-bold text-ink-900">Browse</span>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close menu"
            className="grid h-9 w-9 place-items-center rounded-lg text-ink-500 hover:bg-ink-100"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <path d="M6 6l12 12M18 6 6 18" strokeLinecap="round" />
            </svg>
          </button>
        </div>

        <nav className="p-2">
          <Link
            to="/shop"
            onClick={onClose}
            className="block rounded-lg px-3 py-2.5 text-sm font-semibold text-ink-800 hover:bg-ink-50"
          >
            All products
          </Link>

          {categories.map((category) => (
            <div key={category.id} className="mt-1">
              <Link
                to={`/category/${category.slug}`}
                onClick={onClose}
                className="flex items-center justify-between rounded-lg px-3 py-2.5 text-sm font-medium text-ink-700 hover:bg-ink-50"
              >
                {category.name}
                <span className="text-xs text-ink-400">{category.productCount}</span>
              </Link>

              {category.children.length > 0 && (
                <div className="ml-3 border-l border-ink-100 pl-2">
                  {category.children.map((child) => (
                    <Link
                      key={child.id}
                      to={`/category/${child.slug}`}
                      onClick={onClose}
                      className="block rounded-lg px-3 py-2 text-sm text-ink-500 hover:bg-ink-50"
                    >
                      {child.name}
                    </Link>
                  ))}
                </div>
              )}
            </div>
          ))}
        </nav>
      </div>
    </div>
  )
}
