/**
 * API contracts, mirroring the server DTOs.
 *
 * Hand-written rather than generated so the shapes the UI actually consumes stay readable and
 * reviewable. The server serialises camelCase with nulls omitted, which is why optional fields
 * are `?` rather than `| null`.
 */

// ---- Shared ---------------------------------------------------------------------------------

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

// ---- Catalogue ------------------------------------------------------------------------------

export interface CategoryTree {
  id: string
  name: string
  slug: string
  imageUrl?: string
  iconName?: string
  productCount: number
  children: CategoryTree[]
}

export interface Brand {
  id: string
  name: string
  slug: string
  description?: string
  logoUrl?: string
  countryOfOrigin?: string
  productCount: number
  isFeatured: boolean
}

export interface ProductCard {
  id: string
  name: string
  slug: string
  shortDescription?: string
  imageUrl?: string
  thumbnailUrl?: string
  blurHash?: string
  imageWidth: number
  imageHeight: number
  minPrice: number
  maxPrice: number
  compareAtPrice?: number
  inStock: boolean
  ratingAverage: number
  ratingCount: number
  badge?: string
  brandName?: string
  categoryName?: string
  categorySlug?: string
  variantCount: number
  unit: string
  hasPriceRange: boolean
  discountPercent?: number
}

export interface ProductImage {
  id: string
  url: string
  thumbnailUrl?: string
  altText?: string
  width: number
  height: number
  blurHash?: string
  isPrimary: boolean
  displayOrder: number
}

export interface ProductVariant {
  id: string
  name?: string
  sku: string
  barcode?: string
  price: number
  compareAtPrice?: number
  availableQuantity: number
  isSellable: boolean
  isLowStock: boolean
  unit: string
  unitValue?: number
  pricePerUnit?: number
  weightGrams?: number
  isDefault: boolean
  displayOrder: number
  optionValueIds: string[]
}

export interface ProductOptionValue {
  id: string
  value: string
  hexColor?: string
  displayOrder: number
}

export interface ProductOption {
  id: string
  name: string
  displayOrder: number
  values: ProductOptionValue[]
}

export interface Breadcrumb {
  name: string
  slug: string
}

export interface ProductDetail {
  id: string
  name: string
  slug: string
  shortDescription?: string
  description?: string
  badge?: string
  categoryId?: string
  categoryName?: string
  categorySlug?: string
  brandId?: string
  brandName?: string
  brandSlug?: string
  minPrice: number
  maxPrice: number
  inStock: boolean
  totalStock: number
  ratingAverage: number
  ratingCount: number
  metaTitle?: string
  metaDescription?: string
  images: ProductImage[]
  options: ProductOption[]
  variants: ProductVariant[]
  tags: string[]
  breadcrumbs: Breadcrumb[]
}

export interface FacetItem {
  id: string
  name: string
  slug: string
  count: number
}

export interface ProductFacets {
  categories: FacetItem[]
  brands: FacetItem[]
  minPrice: number
  maxPrice: number
  inStockCount: number
  onSaleCount: number
}

export interface ProductSearchResult {
  products: Paged<ProductCard>
  facets?: ProductFacets
}

export interface SearchSuggestion {
  name: string
  slug: string
  thumbnailUrl?: string
  minPrice: number
  type: string
}

export type ProductSort =
  | 'Relevance'
  | 'Newest'
  | 'PriceLowToHigh'
  | 'PriceHighToLow'
  | 'TopRated'
  | 'BestSelling'
  | 'NameAToZ'

// ---- Cart -----------------------------------------------------------------------------------

export interface CartItem {
  id: string
  productVariantId: string
  productId: string
  productName: string
  productSlug: string
  variantName?: string
  sku: string
  imageUrl?: string
  unitPrice: number
  compareAtPrice?: number
  quantity: number
  lineTotal: number
  availableQuantity: number
  isAvailable: boolean
  unit: string
  unitValue?: number
  priceWhenAdded: number
  priceChanged: boolean
}

export interface CartTotals {
  itemCount: number
  uniqueItemCount: number
  subtotal: number
  discountTotal: number
  total: number
  totalWeightKg: number
  currencyCode: string
}

export interface AppliedCoupon {
  code: string
  description?: string
  discountType: number
  value: number
  discountAmount: number
}

export interface Cart {
  id: string
  items: CartItem[]
  totals: CartTotals
  coupon?: AppliedCoupon
  warnings: string[]
}

// ---- Wishlist -------------------------------------------------------------------------------

export interface WishlistItem {
  id: string
  productId: string
  productName: string
  productSlug: string
  imageUrl?: string
  thumbnailUrl?: string
  minPrice: number
  compareAtPrice?: number
  inStock: boolean
  ratingAverage: number
  ratingCount: number
  priceWhenAdded: number
  notifyOnRestock: boolean
  notifyOnPriceDrop: boolean
  createdAt: string
  priceDrop?: number
}

// ---- Checkout & orders ----------------------------------------------------------------------

export interface ShippingQuote {
  methodId: string
  name: string
  description?: string
  rate: number
  estimatedDaysMin: number
  estimatedDaysMax: number
  isFree: boolean
}

export interface CheckoutSummary {
  items: CartItem[]
  subtotal: number
  discountTotal: number
  shippingTotal: number
  grandTotal: number
  currencyCode: string
  coupon?: AppliedCoupon
  shippingOptions: ShippingQuote[]
  blockers: string[]
}

export interface OrderAddress {
  fullName: string
  phone: string
  line1: string
  line2?: string
  district?: string
  city: string
  region?: string
  postalCode?: string
  countryCode: string
}

export interface OrderItem {
  id: string
  productId?: string
  productVariantId?: string
  productName: string
  variantName?: string
  sku: string
  imageUrl?: string
  productSlug?: string
  unitPrice: number
  quantity: number
  lineDiscount: number
  lineTotal: number
  isReviewed: boolean
}

export interface OrderTimelineEntry {
  status: number
  note?: string
  createdAt: string
}

export interface Shipment {
  id: string
  carrier: string
  trackingNumber?: string
  trackingUrl?: string
  status: number
  shippedAt?: string
  estimatedDeliveryAt?: string
  deliveredAt?: string
}

export interface OrderDetail {
  id: string
  orderNumber: string
  status: number
  paymentStatus: number
  fulfillmentStatus: number
  email: string
  phone: string
  shippingAddress: OrderAddress
  billingAddress: OrderAddress
  subtotal: number
  discountTotal: number
  shippingTotal: number
  taxTotal: number
  grandTotal: number
  currencyCode: string
  couponCode?: string
  shippingMethodName?: string
  paymentMethod: number
  customerNote?: string
  placedAt: string
  confirmedAt?: string
  shippedAt?: string
  deliveredAt?: string
  cancelledAt?: string
  cancelReason?: string
  isCancellable: boolean
  items: OrderItem[]
  timeline: OrderTimelineEntry[]
  shipments: Shipment[]
}

export interface OrderSummary {
  id: string
  orderNumber: string
  status: number
  paymentStatus: number
  fulfillmentStatus: number
  grandTotal: number
  currencyCode: string
  itemCount: number
  firstItemImage?: string
  placedAt: string
}

export interface Address {
  id: string
  label?: string
  fullName: string
  phone: string
  line1: string
  line2?: string
  district?: string
  city: string
  region?: string
  postalCode?: string
  countryCode: string
  isDefaultShipping: boolean
  isDefaultBilling: boolean
}

// ---- Reviews --------------------------------------------------------------------------------

export interface Review {
  id: string
  productId: string
  customerName: string
  customerAvatar?: string
  rating: number
  title?: string
  body: string
  isVerifiedPurchase: boolean
  helpfulCount: number
  adminReply?: string
  adminRepliedAt?: string
  createdAt: string
}

export interface RatingSummary {
  average: number
  total: number
  fiveStar: number
  fourStar: number
  threeStar: number
  twoStar: number
  oneStar: number
}

export interface ProductReviews {
  summary: RatingSummary
  reviews: Paged<Review>
  canReview: boolean
  cannotReviewReason?: string
}

// ---- Identity -------------------------------------------------------------------------------

export interface CurrentUser {
  id: string
  email: string
  firstName: string
  lastName: string
  fullName: string
  phone?: string
  avatarUrl?: string
  preferredLanguage: string
  emailConfirmed: boolean
  roles: string[]
  permissions: string[]
  isSystem: boolean
  isStaff: boolean
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: CurrentUser
}

// ---- Storefront shell -----------------------------------------------------------------------

export interface ContentPage {
  id: string
  title: string
  slug: string
  body: string
  status: number
  metaTitle?: string
  metaDescription?: string
  showInFooter: boolean
  displayOrder: number
  isSystemPage: boolean
}

export interface Banner {
  id: string
  title: string
  subtitle?: string
  imageUrl: string
  mobileImageUrl?: string
  width: number
  height: number
  blurHash?: string
  linkUrl?: string
  buttonText?: string
  position: number
  displayOrder: number
  isActive: boolean
  altText?: string
}

export interface StorefrontBootstrap {
  settings: Record<string, string>
  categories: CategoryTree[]
  footerPages: ContentPage[]
}

// ---- Notifications --------------------------------------------------------------------------

export interface Notification {
  id: string
  type: number
  title: string
  message: string
  linkUrl?: string
  imageUrl?: string
  isRead: boolean
  createdAt: string
}

// ---- Enum mirrors ---------------------------------------------------------------------------
// Numeric, matching the server enums. Kept as const objects rather than TS enums so they erase
// cleanly and add nothing to the bundle.

export const OrderStatus = {
  Pending: 0,
  Confirmed: 1,
  Processing: 2,
  Shipped: 3,
  Delivered: 4,
  Cancelled: 5,
  Refunded: 6,
} as const

export const ORDER_STATUS_LABEL: Record<number, string> = {
  0: 'Pending',
  1: 'Confirmed',
  2: 'Being prepared',
  3: 'On its way',
  4: 'Delivered',
  5: 'Cancelled',
  6: 'Refunded',
}

export const PaymentMethod = {
  CashOnDelivery: 0,
  BankTransfer: 1,
  Card: 2,
  FpsOrWallet: 3,
} as const
