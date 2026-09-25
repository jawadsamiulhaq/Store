# Waqas Provision Store — Architecture

> Production e-commerce platform. React 19 + ASP.NET Core 10 + EF Core 10 + SQL Server.
> The legacy store (`waqasprovisionstore.com`) is a **reference for functionality only**. Design, data model and delivery are new.

---

## 1. Legacy store audit

Measured against the live legacy site on 2026-09-23.

| Area | Legacy behaviour | Measured | Impact |
|---|---|---|---|
| Product list API | Returns the **entire catalogue** on every call, and serialises the same array **twice** (`data` + `products`) while a `pagination` object goes unused. `limit` query param ignored. | **11,343,790 bytes / 50,194 ms** | Catastrophic. Single worst defect. |
| Frontend delivery | Vite SPA, one monolithic chunk, no route splitting, no SSR/prerender, empty `<div id="root">`. | `index-DBy2BCtW.js` = **1,003,445 bytes** | LCP blocked on 1 MB parse+exec; no SEO. |
| Caching | `Cache-Control: no-cache, no-store, must-revalidate, max-age=0` on HTML *and* no CDN/edge layer. | — | Every visit is a cold visit. |
| Variants | None. One price / one stock / `unit` = `"piece"` for **all 4,207 products**. | 4207/4207 `piece` | Cannot sell 500g vs 1kg vs 5kg — core grocery need. |
| Reviews / ratings | Absent from the API surface entirely. | — | No social proof. |
| Wishlist | Absent. | — | Lost re-engagement. |
| Search / filter / sort | No query parameters honoured; filtering is client-side over the 11 MB blob. | — | Unusable on mobile data. |
| Data quality | 205 products on Unsplash stock placeholders; 18 products uncategorised; descriptions all the literal string "Premium quality supermarket grocery item." | 205 / 18 / 4207 | Looks unfinished. |
| Slugs | Random numeric suffixes and doubled separators: `ready---canned-food-639`, `frozen-320`. | — | Bad URLs, bad SEO. |
| Identifiers | Composite pipe-delimited strings: `wq5B3gH54fMCxahE7Bwg\|xJvKgffOX3fpR9z1koGM`. | — | Leaks a prior Firebase/Zobaze POS migration; unsafe in URLs. |
| Images | Firebase Storage originals + Unsplash hotlinks. No `srcset`, no AVIF/WebP, no dimensions. | — | CLS + wasted bytes. |

**Kept from legacy:** catalogue shape (category → product), brands, banners, coupons, inventory adjustments with history, CSV import/migration, order invoices, admin curation of hero/featured/trending, blog/CMS pages, media library, activity logs.

**Fixed:** everything in the table above.

**Added:** variants with option matrix, reviews with verified-purchase, wishlist, real faceted search, shipping zones/methods, refresh-token auth, granular RBAC, notifications, reporting, audit trail, i18n-ready content fields.

---

## 2. Solution layout

```
Store.sln
├─ src/
│  ├─ Store.Domain/          Entities, enums, domain rules. Zero dependencies.
│  ├─ Store.Application/     Services, DTOs, validators, abstractions. Depends on Domain.
│  ├─ Store.Infrastructure/  EF Core, migrations, Identity, caching, email, storage, search.
│  ├─ Store.Api/             Minimal API endpoint groups, middleware, auth, DI composition.
│  └─ store-web/             React 19 + Vite + TypeScript (storefront + admin).
└─ docs/
```

Four projects, one dependency direction: `Api → Infrastructure → Application → Domain`.

**Deliberately excluded as unnecessary complexity:** MediatR, CQRS read/write separation, event sourcing, microservices, repository-over-`DbContext` wrappers, AutoMapper. `DbContext` *is* the unit of work; services return DTOs projected with `Select`.

**Deliberately included:** vertical slicing by module inside `Application` (each module owns its service + DTOs + validators), so the codebase grows by adding folders rather than by editing shared files.

---

## 3. Modules

| Module | Responsibility |
|---|---|
| Identity | Registration, login, refresh rotation, password reset, email confirmation, profile |
| Authorization | Roles, permissions, role-permission and user-permission overrides |
| Catalog | Categories (tree), brands, products, variants, options, images, tags |
| Search | Faceted search, filter, sort, autocomplete, related products |
| Inventory | Stock levels, reservations, adjustments, transaction ledger, low-stock alerts |
| Cart | Anonymous + authenticated carts, merge on login, coupon application |
| Wishlist | Per-customer saved products |
| Checkout | Address, shipping quote, totals calculation, order placement |
| Orders | Lifecycle, status history, shipments, tracking, invoices, cancellation/refund |
| Customers | Profiles, addresses, order history, lifetime value |
| Reviews | Submission, verified-purchase flag, moderation queue, admin replies |
| Promotions | Coupons, redemption limits, discount engine |
| Shipping | Zones, methods, rate calculation |
| Content | Banners, blog posts, CMS pages, media library |
| Notifications | In-app + email, templates, broadcast |
| Reporting | Sales, products, customers, inventory, dashboard KPIs |
| Platform | Settings, audit log, health, background jobs |

---

## 4. Database design

SQL Server. EF Core 10 code-first with explicit `IEntityTypeConfiguration<T>` per entity. `Guid` PKs generated sequentially (`NEWSEQUENTIALID`-style, via `Guid.CreateVersion7()`) to keep clustered-index inserts sequential and avoid page splits — a real throughput difference at 4k+ products and growing order volume.

### Core tables

**Identity & RBAC** — `Users`, `Roles`, `UserRoles`, `Permissions`, `RolePermissions`, `UserPermissions` (grant/deny override), `RefreshTokens`

**Catalog** — `Categories` (self-referencing, materialised `Path` for subtree queries), `Brands`, `Products`, `ProductVariants`, `ProductOptions`, `ProductOptionValues`, `VariantOptionValues`, `ProductImages`, `Tags`, `ProductTags`

**Inventory** — `InventoryTransactions` (append-only ledger; `ProductVariants.StockQuantity` is the maintained projection)

**Commerce** — `Customers`, `Addresses`, `Carts`, `CartItems`, `WishlistItems`, `Orders`, `OrderItems`, `OrderStatusHistory`, `Shipments`, `Payments`

**Promotions & shipping** — `Coupons`, `CouponRedemptions`, `ShippingZones`, `ShippingMethods`

**Engagement** — `Reviews`, `Notifications`

**Content & platform** — `Banners`, `BlogPosts`, `ContentPages`, `MediaAssets`, `Settings`, `AuditLogs`

### Key modelling decisions

1. **Variants carry price and stock, not products.** `Products` holds marketing/SEO; `ProductVariants` holds `Sku`, `Price`, `CompareAtPrice`, `StockQuantity`. Every product gets at least one default variant, so the grocery "500g / 1kg / 5kg" case works without special-casing.
2. **Denormalised `MinPrice` / `MaxPrice` / `InStock` on `Products`**, maintained in the same transaction as variant writes. This turns "sort by price" and "filter in-stock" from a correlated subquery into an indexed column scan.
3. **Denormalised `RatingAverage` / `RatingCount` on `Products`**, maintained on review approval. Avoids aggregating `Reviews` on every catalogue page.
4. **Order line items snapshot** product name, variant name, SKU, image and unit price. Orders must not change when the catalogue changes.
5. **Money is `decimal(18,2)`**; quantities are `int`. Never `float`.
6. **Soft delete** (`DeletedAt`) on `Products`, `Categories`, `Brands` via a global query filter. Orders are never deleted.
7. **Stock reservation** is explicit: `StockQuantity` minus `ReservedQuantity` is what is sellable. Reservations are created at checkout and released on cancel/expiry, preventing oversell under concurrency.
8. **Optimistic concurrency** via `rowversion` on `ProductVariants`, `Orders`, `Coupons` — the three tables where concurrent writes actually collide.
9. **Append-only audit**: `AuditLogs` and `InventoryTransactions` are never updated.

### Indexing strategy

Indexes exist to serve named queries, not speculatively:

| Index | Serves |
|---|---|
| `Products (Slug) UNIQUE` | Product detail page |
| `Products (Status, DeletedAt) INCLUDE (Name, MinPrice, RatingAverage)` filtered on active | Catalogue listing |
| `Products (CategoryId, Status) INCLUDE (MinPrice, CreatedAt, RatingAverage)` | Category browse + sort |
| `Products (BrandId, Status)` | Brand browse |
| `Products (MinPrice)` filtered active | Price sort / price facet |
| `ProductVariants (Sku) UNIQUE` | SKU lookup, import dedupe |
| `ProductVariants (ProductId, IsActive)` | Variant load |
| `Categories (Slug) UNIQUE`, `Categories (ParentId, DisplayOrder)` | Nav tree |
| `Orders (OrderNumber) UNIQUE` | Tracking lookup |
| `Orders (CustomerId, PlacedAt DESC)` | Customer order history |
| `Orders (Status, PlacedAt DESC)` | Admin queue |
| `CartItems (CartId, ProductVariantId) UNIQUE` | Idempotent add-to-cart |
| `WishlistItems (CustomerId, ProductId) UNIQUE` | Idempotent wishlist toggle |
| `Reviews (ProductId, Status, CreatedAt DESC)` | Product reviews tab |
| `InventoryTransactions (ProductVariantId, CreatedAt DESC)` | Stock history |
| `Coupons (Code) UNIQUE` | Coupon apply |
| **SQL Server Full-Text** on `Products (Name, ShortDescription, Description)` | Search relevance |

---

## 5. RBAC

Three-tier, permission-based, enforced on **both** ends.

### Roles

| Role | Model |
|---|---|
| **System** | Unrestricted. Bypasses permission checks entirely — the authorization handler short-circuits to success. Cannot be deleted or stripped of the role. Can manage Admins. |
| **Admin** | **Permission-based only.** Holds zero implicit rights; every capability comes from an explicitly granted permission. |
| **Customer** | Storefront scope. Owns only their own cart, wishlist, addresses, orders, and reviews — enforced by resource-ownership checks, not just role checks. |

### Permission model

Permissions are `module.action` strings, seeded and immutable in code (`Permissions.Products.Create`):

```
users.view|create|update|delete|assign-roles
roles.view|create|update|delete|assign-permissions
products.view|create|update|delete|publish|import
categories.view|create|update|delete
brands.view|create|update|delete
inventory.view|adjust|view-history
orders.view|update-status|cancel|refund|view-invoice|export
customers.view|update|delete|impersonate
reviews.view|moderate|reply|delete
coupons.view|create|update|delete
shipping.view|manage
content.view|manage
media.view|upload|delete
notifications.view|send
reports.view|export
settings.view|manage
audit.view
```

Resolution order for an effective permission set:

1. `System` role → allow everything, stop.
2. Union of `RolePermissions` for all of the user's roles.
3. Apply `UserPermissions` overrides — an explicit `IsGranted = false` **denies** even if a role grants it (deny wins).

The resolved set is cached per user (`HybridCache`, 10 min) and invalidated on any role/permission write.

### Backend enforcement

- ASP.NET Core Identity + JWT bearer. Access token 15 min, refresh token 7 days with **rotation and reuse detection** (a replayed refresh token revokes the whole family).
- A `PermissionAuthorizationHandler` backs a `[RequirePermission("products.create")]` attribute / `.RequirePermission(...)` endpoint extension.
- Permissions are **not** stuffed into the JWT (they'd go stale and bloat every request); they are resolved server-side from cache per request.
- Resource ownership is verified in the service layer for every customer-scoped read and write. A customer cannot fetch another customer's order by guessing an id.

### Frontend enforcement

- `AuthProvider` fetches the effective permission set once after login (`/api/auth/me`).
- `<Can permission="products.create">` gates UI; `<ProtectedRoute permission=...>` gates routes; admin navigation is generated from the permission set.
- Frontend gating is **UX only**. Every gated action re-checks on the server. The UI never holds the security boundary.

---

## 6. Performance strategy

Targets: Lighthouse ≥90 desktop / ≥85 mobile, LCP ≤2.5 s, INP ≤200 ms, CLS ≤0.1, API p95 <300 ms.

### Backend

- **Pagination is mandatory and enforced.** Page size is clamped server-side (default 24, max 100). There is no code path that returns an unbounded collection. Keyset pagination for deep catalogue scrolls.
- **Projection at the database.** Every list query is `.Select(x => new Dto { ... })` — never load entities and map in memory. `AsNoTracking()` on all reads.
- **No N+1, structurally.** List endpoints project the primary image and price range in the same SQL statement via correlated subqueries the provider can fold in; they do not `Include` collections. EF Core's `QuerySplittingBehavior` is set explicitly where `Include` is genuinely needed (product detail).
- **Denormalised aggregates** (`MinPrice`, `RatingAverage`, `InStock`, `Products.ReviewCount`) so catalogue queries never aggregate at read time.
- **`HybridCache`** (in-memory L1, ready for Redis L2) on category tree, brand list, settings, shipping methods, permission sets — data read constantly and written rarely. Tag-based invalidation on write.
- **Output caching** on anonymous catalogue GETs, varied by query string, with ETag/304 support.
- **Compiled queries** for the hottest paths (product by slug, catalogue page).
- **Brotli + Gzip response compression**; JSON is already compact (camelCase, nulls omitted, no duplicated arrays — the legacy bug that doubled every payload cannot recur because responses are typed DTOs).
- **Rate limiting** per IP and per user to protect tail latency.
- **Connection resiliency + command timeouts**; `MARS` off; pooled `DbContext`.
- A **slow-query middleware** logs any request over 300 ms with its route and duration, so regressions surface in logs rather than in user complaints.

### Frontend

- **Route-level code splitting** via `React.lazy` on every page. Storefront and admin are separate lazy trees — a customer never downloads a byte of admin code. This alone is the difference from legacy's 1 MB monolith.
- **Manual vendor chunking** (react / router / query / motion split apart) so a page change doesn't bust the vendor cache.
- **TanStack Query** with `staleTime` tuned per resource, plus **prefetch on link hover/viewport** so navigation feels instant without extra initial JS.
- **Images:** AVIF/WebP with `srcset` + `sizes`, explicit `width`/`height` on every `<img>` to hold layout (CLS), `loading="lazy"` + `decoding="async"` below the fold, `fetchpriority="high"` on the LCP image only, and a blur/skeleton placeholder that reserves exact space.
- **Fonts:** system-ui stack for body text (zero download), one self-hosted subset variable display face, preloaded, `font-display: swap`, with metric-matched fallback to prevent reflow.
- **Animations are transform/opacity only**, GPU-composited, never animating layout-affecting properties. `prefers-reduced-motion` respected throughout. Motion library is lazy-loaded and absent from the critical path.
- **Skeletons match final dimensions exactly** so content swap causes no shift.
- **Virtualised lists** for admin tables and long result sets.
- **CDN-ready:** hashed immutable asset filenames, `Cache-Control: public, max-age=31536000, immutable` for `/assets/*`, short-TTL revalidated HTML.
- **Minimal requests:** the storefront home page is served by a single aggregated `/api/storefront/home` call rather than six parallel ones.

### Verification

Performance is tested before the build is called done: Lighthouse CI against a production build for desktop and mobile, `k6` load test asserting API p95, and EF Core query logging reviewed for N+1 and unbounded reads.

---

## 7. Cross-cutting

- **Validation** — FluentValidation on every input DTO; a single middleware converts failures to RFC 9457 `ProblemDetails`.
- **Error handling** — global exception handler; typed domain exceptions map to status codes; stack traces never cross the wire in production; every response carries a correlation id.
- **Logging** — Serilog, structured, with request/correlation enrichment, rolling file + console sinks.
- **Migrations** — EF Core migrations checked into source, applied via a guarded startup migrator in development and an explicit script in production.
- **Security** — HTTPS, HSTS, CSP and security headers, CORS allowlist, Argon2-grade password hashing via Identity, lockout on repeated failure, anti-automation rate limits, parameterised queries throughout, no secrets in source.
