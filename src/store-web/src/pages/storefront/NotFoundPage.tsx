import { Link } from 'react-router-dom'
import { ButtonLink } from '../../ui/primitives'
import { useStore } from '../../app/providers/StoreProvider'

export default function NotFoundPage() {
  const { categories } = useStore()

  return (
    <div className="mx-auto flex max-w-2xl flex-col items-center px-4 py-20 text-center sm:px-6">
      <span className="font-display text-7xl font-bold tracking-tight text-saffron-300">404</span>

      <h1 className="mt-4 text-2xl font-bold tracking-tight text-ink-900 sm:text-3xl">
        We could not find that page
      </h1>
      <p className="mt-2 max-w-md text-ink-500">
        The link may be out of date, or the product may have been removed from the shelves.
      </p>

      <div className="mt-8 flex flex-wrap justify-center gap-3">
        <ButtonLink to="/">Back to the shop</ButtonLink>
        <ButtonLink to="/shop" variant="outline">
          Browse all products
        </ButtonLink>
      </div>

      {/* A dead end is a good place to offer a way onward rather than just an apology. */}
      {categories.length > 0 && (
        <div className="mt-12 w-full border-t border-ink-100 pt-8">
          <h2 className="text-sm font-semibold uppercase tracking-wide text-ink-500">
            Popular aisles
          </h2>
          <div className="mt-4 flex flex-wrap justify-center gap-2">
            {categories.slice(0, 8).map((category) => (
              <Link
                key={category.id}
                to={`/category/${category.slug}`}
                className="rounded-full border border-ink-200 bg-paper-raised px-3.5 py-1.5 text-sm text-ink-600 transition-colors hover:border-saffron-300 hover:text-saffron-700"
              >
                {category.name}
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
