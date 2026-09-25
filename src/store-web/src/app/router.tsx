import { lazy, Suspense } from 'react'
import { createBrowserRouter, Outlet } from 'react-router-dom'
import { StoreLayout } from './layouts/StoreLayout'
import { RouteFallback } from './components/RouteFallback'
import { RequireAuth, RequirePermission, RequireStaff } from './components/RouteGuards'

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
const AdminProductFormPage = lazy(() => import('../pages/admin/AdminProductFormPage'))
const AdminCategoriesPage = lazy(() => import('../pages/admin/AdminCategoriesPage'))
const AdminBrandsPage = lazy(() => import('../pages/admin/AdminBrandsPage'))
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

/** An admin route: suspended for its lazy chunk, then gated on the permission it needs. */
function guarded(permission: string, element: React.ReactNode) {
  return <RequirePermission permission={permission}>{withSuspense(element)}</RequirePermission>
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
    /*
      Every admin route declares the permission it needs, matching the constant the endpoints
      behind it are gated on in `Store.Domain/Identity/Permissions.cs`. `RequireStaff` above only
      establishes that someone belongs in the admin area at all; without the per-route check a
      staff member holding one permission could open every screen and meet a wall of 403s.

      Where a screen creates as well as reads, the *view* permission gates the route and the
      create/update buttons inside it are hidden separately — so someone with read-only access
      still gets a useful page rather than a refusal.
    */
    children: [
      { index: true, element: guarded('reports.view', <DashboardPage />) },
      { path: 'products', element: guarded('products.view', <AdminProductsPage />) },

      // 'new' is declared before ':id' so it is never captured as an id.
      { path: 'products/new', element: guarded('products.create', <AdminProductFormPage />) },
      { path: 'products/:id/edit', element: guarded('products.view', <AdminProductFormPage />) },

      { path: 'categories', element: guarded('categories.view', <AdminCategoriesPage />) },
      { path: 'brands', element: guarded('brands.view', <AdminBrandsPage />) },
      { path: 'orders', element: guarded('orders.view', <AdminOrdersPage />) },
      { path: 'inventory', element: guarded('inventory.view', <AdminInventoryPage />) },
      { path: 'customers', element: guarded('customers.view', <AdminCustomersPage />) },
      { path: 'reviews', element: guarded('reviews.view', <AdminReviewsPage />) },
      { path: 'coupons', element: guarded('coupons.view', <AdminCouponsPage />) },
      { path: 'users', element: guarded('users.view', <AdminUsersPage />) },
      { path: 'settings', element: guarded('settings.view', <AdminSettingsPage />) },
      { path: '*', element: withSuspense(<NotFoundPage />) },
    ],
  },
])

/** Re-exported so nested layouts can render their children without importing react-router. */
export { Outlet }
