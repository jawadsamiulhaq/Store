/**
 * Shown while a lazy route chunk downloads.
 *
 * Deliberately not a spinner. A spinner communicates "something is happening" but reserves no
 * space, so the real page lands into an empty container and shifts everything — the exact cause
 * of a poor CLS score. This reserves a realistic page-shaped area instead, so the swap is a
 * content change rather than a layout change.
 */
export function RouteFallback() {
  return (
    <div className="mx-auto w-full max-w-7xl px-4 py-10 sm:px-6 lg:px-8" aria-busy="true" aria-live="polite">
      <span className="sr-only">Loading…</span>

      <div className="skeleton h-9 w-64" />

      <div className="mt-8 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
        {Array.from({ length: 8 }, (_, index) => (
          <div key={index} className="space-y-3">
            {/* Square, matching the product card's aspect-square image slot exactly. */}
            <div className="skeleton aspect-square w-full" />
            <div className="skeleton h-4 w-3/4" />
            <div className="skeleton h-4 w-1/2" />
          </div>
        ))}
      </div>
    </div>
  )
}

/** Compact inline variant for panels and drawers. */
export function InlineFallback({ rows = 3 }: { rows?: number }) {
  return (
    <div className="space-y-3" aria-busy="true">
      <span className="sr-only">Loading…</span>
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="skeleton h-16 w-full" />
      ))}
    </div>
  )
}
