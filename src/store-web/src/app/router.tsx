import { lazy, Suspense } from 'react'
import { createBrowserRouter, Outlet } from 'react-router-dom'
import { StoreLayout } from './layouts/StoreLayout'
import { RouteFallback } from './components/RouteFallback'
import { RequireAuth, RequireStaff } from './components/RouteGuards'

/*
  Route-level code splitting.
  ===========================

  Every page is lazy. This is the single most important structural difference from the legacy
  store, which shipped one 1,003,445-byte chunk containing the entire storefront *and* the entire
  admin area — so a shopper browsing rice downloaded the whole product editor, the order queue and
  the user-permission matrix before anything rendered.

  Here the admin tree is a completely separate lazy subtree: a customer never downloads a byte of
  it. Within the storefront, a visitor landing on the home page downloads the home page, not the
  checkout.
*/

// ---- Storefront -------------------------------------------------------------------------------
const HomePage = lazy(() => import('../pages/storefront/HomePage'))
const CatalogPage = lazy(() => import('../pages/storefront/CatalogPage'))
const ProductPage = lazy(() => import('../pages/storefront/ProductPage'))
const CartPage = lazy(() => import('../pages/storefront/CartPage'))
const CheckoutPage = lazy(() => import('../pages/storefront/CheckoutPage'))
const OrderConfirmationPage = lazy(() => import('../pages/storefront/OrderConfirmationPage'))
const TrackOrderPage = lazy(() => import('../pages/storefront/TrackOrderPage'))
const ContentPage = lazy(() => import('../pages/storefront/ContentPage'))
const NotFoundPage = lazy(() => import('../pages/storefront/NotFoundPage'))

// ---- Account ----------------------------------------------------------------------------------
const LoginPage = lazy(() => import('../pages/account/LoginPage'))
const RegisterPage = lazy(() => import('../pages/account/RegisterPage'))
const AccountLayout = lazy(() => import('./layouts/AccountLayout'))
const ProfilePage = lazy(() => import('../pages/account/ProfilePage'))
const OrdersPage = lazy(() => import('../pages/account/OrdersPage'))
const OrderDetailPage = lazy(() => import('../pages/account/OrderDetailPage'))
const AddressesPage = lazy(() => import('../pages/account/AddressesPage'))
const WishlistPage = lazy(() => import('../pages/account/WishlistPage'))

// ---- Admin ------------------------------------------------------------------------------------
// A separate lazy subtree. Nothing below this line reaches a customer's browser.
const AdminLayout = lazy(() => import('./layouts/AdminLayout'))
const DashboardPage = lazy(() => import('../pages/admin/DashboardPage'))
const AdminProductsPage = lazy(() => import('../pages/admin/AdminProductsPage'))
const AdminOrdersPage = lazy(() => import('../pages/admin/AdminOrdersPage'))
const AdminInventoryPage = lazy(() => import('../pages/admin/AdminInventoryPage'))
const AdminCustomersPage = lazy(() => import('../pages/admin/AdminCustomersPage'))
const AdminReviewsPage = lazy(() => import('../pages/admin/AdminReviewsPage'))
const AdminCouponsPage = lazy(() => import('../pages/admin/AdminCouponsPage'))
const AdminUsersPage = lazy(() => import('../pages/admin/AdminUsersPage'))
const AdminSettingsPage = lazy(() => import('../pages/admin/AdminSettingsPage'))

/** Wraps a lazy element in the shared fallback so each route does not repeat the boilerplate. */
function withSuspense(element: React.ReactNode) {
  return <Suspense fallback={<RouteFallback />}>{element}</Suspense>
}

export const router = createBrowserRouter([
  {
    path: '/',
    element: <StoreLayout />,
    children: [
      { index: true, element: withSuspense(<HomePage />) },

      // Both shapes hit the same page; the catalogue reads its filters from the URL, which is
      // what makes a filtered view shareable and back-button correct.
      { path: 'shop', element: withSuspense(<CatalogPage />) },
      { path: 'category/:slug', element: withSuspense(<CatalogPage />) },
      { path: 'brand/:slug', element: withSuspense(<CatalogPage />) },
      { path: 'search', element: withSuspense(<CatalogPage />) },

      { path: 'product/:slug', element: withSuspense(<ProductPage />) },
      { path: 'cart', element: withSuspense(<CartPage />) },
      { path: 'checkout', element: withSuspense(<CheckoutPage />) },
      { path: 'order-confirmation/:id', element: withSuspense(<OrderConfirmationPage />) },
      { path: 'track', element: withSuspense(<TrackOrderPage />) },
      { path: 'page/:slug', element: withSuspense(<ContentPage />) },

      { path: 'login', element: withSuspense(<LoginPage />) },
      { path: 'register', element: withSuspense(<RegisterPage />) },

      {
        path: 'account',
        element: (
          <RequireAuth>
            <Suspense fallback={<RouteFallback />}>
              <AccountLayout />
            </Suspense>
          </RequireAuth>
        ),
        children: [
          { index: true, element: withSuspense(<ProfilePage />) },
          { path: 'orders', element: withSuspense(<OrdersPage />) },
          { path: 'orders/:id', element: withSuspense(<OrderDetailPage />) },
          { path: 'addresses', element: withSuspense(<AddressesPage />) },
          { path: 'wishlist', element: withSuspense(<WishlistPage />) },
        ],
      },

      { path: '*', element: withSuspense(<NotFoundPage />) },
    ],
  },

  {
    path: '/admin',
    element: (
      <RequireStaff>
        <Suspense fallback={<RouteFallback />}>
          <AdminLayout />
        </Suspense>
      </RequireStaff>
    ),
    children: [
      { index: true, element: withSuspense(<DashboardPage />) },
      { path: 'products', element: withSuspense(<AdminProductsPage />) },
      { path: 'orders', element: withSuspense(<AdminOrdersPage />) },
      { path: 'inventory', element: withSuspense(<AdminInventoryPage />) },
      { path: 'customers', element: withSuspense(<AdminCustomersPage />) },
      { path: 'reviews', element: withSuspense(<AdminReviewsPage />) },
      { path: 'coupons', element: withSuspense(<AdminCouponsPage />) },
      { path: 'users', element: withSuspense(<AdminUsersPage />) },
      { path: 'settings', element: withSuspense(<AdminSettingsPage />) },
      { path: '*', element: withSuspense(<NotFoundPage />) },
    ],
  },
])

/** Re-exported so nested layouts can render their children without importing react-router. */
export { Outlet }
