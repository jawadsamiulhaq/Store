import { useState } from 'react'

/**
 * Stand-in for a product with no photograph yet.
 *
 * A generic "broken image" glyph makes a whole catalogue look broken — which is exactly how this
 * grid read before, because the demo data references photos that do not exist. This instead draws
 * a deterministic, branded tile: a soft two-tone wash plus the product's initials, with the hue
 * derived from the name so the same product always gets the same tile and a grid of them looks
 * varied rather than repetitive.
 *
 * It costs no network request and no layout shift, and it reads as "photo pending" rather than
 * "this site is broken".
 */
function PlaceholderTile({
  label,
  ratio,
  className,
}: {
  label: string
  ratio: string
  className: string
}) {
  // Cheap deterministic hash — stable across renders and across machines.
  let hash = 0
  for (let i = 0; i < label.length; i++) {
    hash = (hash * 31 + label.charCodeAt(i)) | 0
  }

  // Cool band only (205°–240°), so tiles sit inside the indigo/paper palette instead of
  // introducing arbitrary colours that fight the brand. A grid of these reads as a quiet
  // placeholder field rather than as a second palette competing with the real photography.
  const hue = 205 + (Math.abs(hash) % 35)

  const initials = label
    .split(/\s+/)
    .filter((word) => /[a-z0-9]/i.test(word[0] ?? ''))
    .slice(0, 2)
    .map((word) => word[0]!.toUpperCase())
    .join('')

  return (
    <div
      className={`relative flex items-center justify-center overflow-hidden ${className}`}
      style={{
        aspectRatio: ratio,
        backgroundImage:
          `linear-gradient(145deg, hsl(${hue} 46% 94%) 0%, hsl(${hue + 8} 38% 88%) 100%)`,
      }}
      role="img"
      aria-label={label}
    >
      {/*
        Rings and initials both live inside one viewBox, so they scale with the tile automatically
        — the same component serves a 40px cart thumbnail and an 800px product image without any
        breakpoint logic or container queries.
      */}
      <svg
        className="absolute inset-0 h-full w-full"
        viewBox="0 0 100 100"
        preserveAspectRatio="xMidYMid slice"
        aria-hidden="true"
      >
        <g opacity="0.10" stroke={`hsl(${hue} 55% 35%)`} fill="none" strokeWidth="0.6">
          <circle cx="50" cy="50" r="34" />
          <circle cx="50" cy="50" r="24" />
        </g>

        <text
          x="50"
          y="50"
          textAnchor="middle"
          dominantBaseline="central"
          fontSize="26"
          fontWeight="700"
          letterSpacing="-1"
          fill={`hsl(${hue} 42% 44%)`}
          // Matches the display face used for headings elsewhere.
          fontFamily="var(--font-display)"
        >
          {initials || '·'}
        </text>
      </svg>
    </div>
  )
}

interface ImageProps {
  src?: string
  alt: string
  /** Intrinsic width. Required — see the note below. */
  width: number
  /** Intrinsic height. Required — see the note below. */
  height: number
  /** Tiny base64 LQIP rendered behind the image while it decodes. */
  blurHash?: string
  className?: string
  sizes?: string
  /**
   * Set on the LCP image only — typically the hero, or the first product image on a detail page.
   * Marking several images as priority defeats the purpose by splitting bandwidth between them.
   */
  priority?: boolean
}

/**
 * The only way images are rendered in this app.
 *
 * Three things here are load-bearing for the performance targets, and all three were missing from
 * the legacy store (which hotlinked unsized Firebase and Unsplash originals):
 *
 * 1. **`width` and `height` are required props.** With both present the browser computes the
 *    aspect ratio and reserves the exact box before a single byte arrives. This is what keeps CLS
 *    at zero — a late-loading image pushes nothing down.
 *
 * 2. **`loading` and `fetchPriority` are set deliberately.** Below-the-fold images are lazy and
 *    low priority; the one LCP image is eager and high priority, so it is not queued behind a
 *    dozen thumbnails it visually precedes.
 *
 * 3. **`decoding="async"`** keeps image decode off the main thread, which protects INP on
 *    scroll-heavy catalogue pages.
 */
export function Image({
  src,
  alt,
  width,
  height,
  blurHash,
  className = '',
  sizes,
  priority = false,
}: ImageProps) {
  const [loaded, setLoaded] = useState(false)
  const [failed, setFailed] = useState(false)

  // The wrapper owns the reserved space via aspect-ratio, so the box is correct even before the
  // <img> has any intrinsic size of its own.
  const ratio = width > 0 && height > 0 ? `${width} / ${height}` : '1 / 1'

  if (!src || failed) {
    return <PlaceholderTile label={alt} ratio={ratio} className={className} />
  }

  return (
    <div
      className={`relative overflow-hidden bg-paper-sunken ${className}`}
      style={{ aspectRatio: ratio }}
    >
      {/*
        The placeholder sits behind the image and fades out once it has decoded. It is a
        background-image rather than a second <img> so it costs no extra request and cannot
        itself shift anything.
      */}
      {blurHash && !loaded && (
        <div
          className="absolute inset-0 scale-110 blur-xl"
          style={{
            backgroundImage: `url(${blurHash})`,
            backgroundSize: 'cover',
            backgroundPosition: 'center',
          }}
          aria-hidden="true"
        />
      )}

      <img
        src={src}
        alt={alt}
        width={width}
        height={height}
        sizes={sizes}
        loading={priority ? 'eager' : 'lazy'}
        fetchPriority={priority ? 'high' : 'auto'}
        decoding="async"
        onLoad={() => setLoaded(true)}
        onError={() => setFailed(true)}
        // Opacity only — a transform or size transition here would animate layout.
        className={`h-full w-full object-cover transition-opacity duration-300 ${
          loaded ? 'opacity-100' : 'opacity-0'
        }`}
      />
    </div>
  )
}
