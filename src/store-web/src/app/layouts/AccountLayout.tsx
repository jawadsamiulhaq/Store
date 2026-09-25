import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../providers/AuthProvider'

const NAV = [
  { to: '/account', label: 'Profile', end: true },
  { to: '/account/orders', label: 'My orders' },
  { to: '/account/addresses', label: 'Addresses' },
  { to: '/account/wishlist', label: 'Saved items' },
] as const

export default function AccountLayout() {
  const { user, logout } = useAuth()

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <header>
        <h1 className="text-2xl font-bold tracking-tight text-ink-900">My account</h1>
        <p className="mt-1 text-sm text-ink-500">
          Signed in as <span className="font-medium text-ink-700">{user?.email}</span>
        </p>
      </header>

      <div className="mt-6 gap-8 lg:flex">
        <nav aria-label="Account" className="lg:w-56 lg:shrink-0">
          {/*
            Horizontal scroller on mobile, vertical list on desktop. A wrapping row of tabs on a
            narrow screen changes height as it wraps, which shifts the content beneath it.
          */}
          <ul className="no-scrollbar flex gap-1 overflow-x-auto pb-2 lg:flex-col lg:overflow-visible lg:pb-0">
            {NAV.map((item) => (
              <li key={item.to} className="shrink-0 lg:shrink">
                <NavLink
                  to={item.to}
                  end={'end' in item ? item.end : false}
                  className={({ isActive }) =>
                    `block whitespace-nowrap rounded-lg px-3.5 py-2 text-sm font-medium transition-colors ${
                      isActive
                        ? 'bg-saffron-50 text-saffron-700'
                        : 'text-ink-600 hover:bg-ink-100'
                    }`
                  }
                >
                  {item.label}
                </NavLink>
              </li>
            ))}

            <li className="shrink-0 lg:mt-4 lg:shrink lg:border-t lg:border-ink-100 lg:pt-4">
              <button
                type="button"
                onClick={() => void logout()}
                className="block w-full whitespace-nowrap rounded-lg px-3.5 py-2 text-left text-sm font-medium text-chilli-600 transition-colors hover:bg-chilli-50"
              >
                Sign out
              </button>
            </li>
          </ul>
        </nav>

        {/* min-w-0 stops a wide table inside from blowing out the flex row. */}
        <div className="mt-4 min-w-0 flex-1 lg:mt-0">
          <Outlet />
        </div>
      </div>
    </div>
  )
}
