import { useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../providers/AuthProvider'
import { useStore } from '../providers/StoreProvider'

/**
 * Admin navigation, declared with the permission each entry requires.
 *
 * The menu is *generated* from the signed-in user's effective permission set, so an administrator
 * only ever sees the sections they can actually use — rather than a full menu that produces 403s
 * when clicked. This is a usability decision, not a security one: every route behind these links
 * is guarded again on the client and enforced again on the server.
 */
const NAV = [
  { to: '/admin', label: 'Dashboard', permission: 'reports.view', end: true, icon: 'grid' },
  { to: '/admin/orders', label: 'Orders', permission: 'orders.view', icon: 'bag' },
  { to: '/admin/products', label: 'Products', permission: 'products.view', icon: 'box' },
  { to: '/admin/categories', label: 'Categories', permission: 'categories.view', icon: 'tree' },
  { to: '/admin/brands', label: 'Brands', permission: 'brands.view', icon: 'badge' },
  { to: '/admin/inventory', label: 'Inventory', permission: 'inventory.view', icon: 'layers' },
  { to: '/admin/customers', label: 'Customers', permission: 'customers.view', icon: 'users' },
  { to: '/admin/reviews', label: 'Reviews', permission: 'reviews.view', icon: 'star' },
  { to: '/admin/coupons', label: 'Coupons', permission: 'coupons.view', icon: 'tag' },
  { to: '/admin/users', label: 'Users & roles', permission: 'users.view', icon: 'shield' },
  { to: '/admin/settings', label: 'Settings', permission: 'settings.view', icon: 'cog' },
] as const

const ICONS: Record<string, string> = {
  grid: 'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z',
  bag: 'M4 8h16l-1.3 11a2 2 0 0 1-2 1.8H7.3a2 2 0 0 1-2-1.8ZM9 8V6a3 3 0 0 1 6 0v2',
  box: 'M21 8 12 3 3 8l9 5 9-5ZM3 8v8l9 5 9-5V8',
  tree: 'M5 4v10a2 2 0 0 0 2 2h4M5 4h14M11 10h8M11 16h8M5 4v0',
  badge: 'M12 2.8 19 6v5.5c0 4-2.9 7.5-7 8.7-4.1-1.2-7-4.7-7-8.7V6l7-3.2ZM9.5 12l1.8 1.8 3.4-3.6',
  layers: 'm12 3 9 5-9 5-9-5 9-5ZM3 13l9 5 9-5M3 17l9 5 9-5',
  users: 'M16 19v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 9a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7ZM22 19v-2a4 4 0 0 0-3-3.9',
  star: 'M12 2.5 14.8 8l6.2.9-4.5 4.4 1.1 6.2L12 16.6 6.4 19.5l1.1-6.2L3 8.9 9.2 8 12 2.5Z',
  tag: 'M3 12V4a1 1 0 0 1 1-1h8l9 9-9 9-9-9ZM7.5 7.5h.01',
  shield: 'M12 3l8 3v6c0 5-3.4 8.4-8 9.5C7.4 20.4 4 17 4 12V6l8-3Z',
  cog: 'M12 15.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7ZM19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-1.8-.3 1.6 1.6 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1A1.6 1.6 0 0 0 9 19.4a1.6 1.6 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0 .3-1.8 1.6 1.6 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1A1.6 1.6 0 0 0 4.6 9a1.6 1.6 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H9a1.6 1.6 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 1 1.5 1.6 1.6 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V9a1.6 1.6 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1Z',
}

export default function AdminLayout() {
  const location = useLocation()
  const { user, can, logout } = useAuth()
  const { setting } = useStore()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const visible = NAV.filter((item) => can(item.permission))

  return (
    <div className="flex min-h-dvh bg-paper-sunken">
      {/* Mobile backdrop. Rendered only when open so it never intercepts taps otherwise. */}
      {sidebarOpen && (
        <button
          type="button"
          aria-label="Close navigation"
          onClick={() => setSidebarOpen(false)}
          className="animate-fade-in fixed inset-0 z-30 bg-ink-900/40 lg:hidden"
        />
      )}

      <aside
        className={`fixed inset-y-0 left-0 z-40 w-60 shrink-0 border-r border-ink-100 bg-paper-raised transition-transform duration-200 lg:static lg:translate-x-0 ${
          sidebarOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div className="flex h-16 items-center gap-2.5 border-b border-ink-100 px-4">
          <span className="grid h-8 w-8 place-items-center rounded-lg bg-ink-900 text-paper">
            <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <path d="M4 8h16l-1.3 11a2 2 0 0 1-2 1.8H7.3a2 2 0 0 1-2-1.8Z" strokeLinejoin="round" />
            </svg>
          </span>
          <div className="min-w-0">
            <p className="truncate font-display text-sm font-bold text-ink-900">Admin</p>
            <p className="truncate text-[10px] text-ink-400">{setting('store.name', 'Waqas Provision Store')}</p>
          </div>
        </div>

        <nav className="p-2">
          <ul className="space-y-0.5">
            {visible.map((item) => (
              <li key={item.to}>
                <NavLink
                  to={item.to}
                  end={'end' in item ? item.end : false}
                  onClick={() => setSidebarOpen(false)}
                  className={({ isActive }) =>
                    `flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                      isActive ? 'bg-saffron-50 text-saffron-700' : 'text-ink-600 hover:bg-ink-50'
                    }`
                  }
                >
                  <svg className="h-4 w-4 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
                    <path d={ICONS[item.icon]} strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>

          {/*
            Shown when an administrator holds no admin permissions at all — rather than an empty
            sidebar, which looks like the app is broken.
          */}
          {visible.length === 0 && (
            <p className="px-3 py-4 text-xs leading-relaxed text-ink-400">
              You do not have any admin permissions yet. Ask a System user to grant them.
            </p>
          )}
        </nav>

        <div className="absolute inset-x-0 bottom-0 border-t border-ink-100 p-3">
          <div className="flex items-center gap-2.5 px-1">
            <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-saffron-100 text-xs font-bold text-saffron-700">
              {user?.firstName?.[0]?.toUpperCase() ?? 'A'}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-xs font-medium text-ink-800">{user?.fullName}</p>
              <p className="truncate text-[10px] text-ink-400">{user?.roles.join(', ')}</p>
            </div>
          </div>

          <div className="mt-2 flex gap-1">
            <Link
              to="/"
              className="flex-1 rounded-lg px-2 py-1.5 text-center text-xs text-ink-500 transition-colors hover:bg-ink-50"
            >
              View shop
            </Link>
            <button
              type="button"
              onClick={() => void logout()}
              className="flex-1 rounded-lg px-2 py-1.5 text-center text-xs text-chilli-600 transition-colors hover:bg-chilli-50"
            >
              Sign out
            </button>
          </div>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 items-center gap-3 border-b border-ink-100 bg-paper-raised px-4 lg:hidden">
          <button
            type="button"
            onClick={() => setSidebarOpen(true)}
            aria-label="Open navigation"
            className="grid h-10 w-10 place-items-center rounded-lg text-ink-600"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <path d="M4 6h16M4 12h16M4 18h16" strokeLinecap="round" />
            </svg>
          </button>
          <span className="font-display font-bold text-ink-900">Admin</span>
        </header>

        <main key={location.pathname} className="animate-page-in min-w-0 flex-1 p-4 sm:p-6 lg:p-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
