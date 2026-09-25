import { Outlet, ScrollRestoration, useLocation } from 'react-router-dom'
import { SiteHeader } from '../components/SiteHeader'
import { SiteFooter } from '../components/SiteFooter'

export function StoreLayout() {
  const location = useLocation()

  return (
    <div className="flex min-h-dvh flex-col">
      {/* First focusable element on the page — keyboard users should not tab the whole nav. */}
      <a
        href="#main"
        className="sr-only-focusable absolute left-4 top-4 z-50 rounded-lg bg-ink-900 px-4 py-2 text-sm font-medium text-paper"
      >
        Skip to content
      </a>

      <SiteHeader />

      {/*
        Keyed on the path, not the full location: a key that included the query string would
        replay the entrance every time a catalogue filter changed, which turns adjusting a price
        slider into a flicker. Filters update in place; navigating to a different page animates.
      */}
      <main id="main" key={location.pathname} className="animate-page-in flex-1">
        <Outlet />
      </main>

      <SiteFooter />

      {/*
        Restores scroll position on back/forward navigation. Without it, returning from a product
        to a catalogue page lands at the top, which on page 7 of a grid is genuinely infuriating.
      */}
      <ScrollRestoration />
    </div>
  )
}
