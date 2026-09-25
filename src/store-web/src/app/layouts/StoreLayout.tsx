import { Outlet, ScrollRestoration } from 'react-router-dom'
import { SiteHeader } from '../components/SiteHeader'
import { SiteFooter } from '../components/SiteFooter'

export function StoreLayout() {
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

      <main id="main" className="flex-1">
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
