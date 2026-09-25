import { Link } from 'react-router-dom'
import { Image } from '../../ui/Image'
import { Badge, Button, ButtonLink, EmptyState, Rating } from '../../ui/primitives'
import { useWishlist, useWishlistRemove } from '../../features/wishlist/useWishlist'
import { formatPrice } from '../../lib/format'

export default function WishlistPage() {
  const { data: items, isLoading } = useWishlist()
  const remove = useWishlistRemove()

  if (isLoading) {
    return (
      <div className="space-y-3" aria-busy="true">
        {Array.from({ length: 3 }, (_, index) => (
          <div key={index} className="skeleton h-28 w-full" />
        ))}
      </div>
    )
  }

  if (!items || items.length === 0) {
    return (
      <EmptyState
        title="Nothing saved yet"
        description="Tap the heart on any product to keep it here for later."
        action={<ButtonLink to="/shop">Browse products</ButtonLink>}
      />
    )
  }

  return (
    <ul className="space-y-3">
      {items.map((item) => (
        <li key={item.id} className="card-surface flex gap-4 p-4">
          <Link to={`/product/${item.productSlug}`} className="shrink-0">
            <Image
              src={item.thumbnailUrl ?? item.imageUrl}
              alt={item.productName}
              width={88}
              height={88}
              className="h-22 w-22 rounded-lg"
            />
          </Link>

          <div className="flex min-w-0 flex-1 flex-col">
            <Link
              to={`/product/${item.productSlug}`}
              className="line-clamp-2 text-sm font-medium text-ink-800 hover:text-saffron-600"
            >
              {item.productName}
            </Link>

            {item.ratingCount > 0 && (
              <div className="mt-1">
                <Rating value={item.ratingAverage} count={item.ratingCount} />
              </div>
            )}

            <div className="mt-1.5 flex flex-wrap items-center gap-2">
              <span className="text-base font-semibold text-ink-900">{formatPrice(item.minPrice)}</span>

              {/*
                The reason a wishlist earns its place: a saved item that has since got cheaper is
                worth telling the shopper about, and the server records the price at save time so
                the comparison is real rather than guessed.
              */}
              {item.priceDrop !== undefined && item.priceDrop > 0 && (
                <Badge tone="sale">Down {formatPrice(item.priceDrop)}</Badge>
              )}

              {!item.inStock && <Badge tone="neutral">Out of stock</Badge>}
            </div>

            <div className="mt-auto flex items-center gap-3 pt-2">
              <ButtonLink to={`/product/${item.productSlug}`} size="sm">
                View product
              </ButtonLink>

              <Button
                variant="ghost"
                size="sm"
                onClick={() => remove.mutate(item.productId)}
                disabled={remove.isPending}
              >
                Remove
              </Button>
            </div>
          </div>
        </li>
      ))}
    </ul>
  )
}
