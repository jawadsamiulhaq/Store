import { useEffect, useRef, useState } from 'react'

/**
 * Reveals an element the first time it scrolls into view.
 *
 * Used on the home page's lower sections, which are below the fold on every screen size — so the
 * animation costs nothing at load and only runs for content the shopper is actually arriving at.
 *
 * Three things make this safe rather than decorative-but-fragile:
 *
 * 1. **The hidden state is applied by JavaScript, never by the stylesheet.** If the observer never
 *    runs — an old browser, a script error, a crawler — the element was simply never hidden.
 *    Doing it the other way round (hidden in CSS, revealed by JS) risks a permanently invisible
 *    page, which is a far worse failure than a missing animation.
 *
 * 2. **It disconnects after the first intersection.** A reveal that replays on every scroll past
 *    is the single most irritating version of this effect, and an observer left attached to
 *    dozens of nodes is work the main thread does not need.
 *
 * 3. **It honours `prefers-reduced-motion` at the source.** The global media query already
 *    collapses the transition, but checking here means the hidden state is never applied at all,
 *    so there is nothing to collapse.
 */
export function useReveal<T extends HTMLElement = HTMLDivElement>() {
  const ref = useRef<T>(null)
  const [shown, setShown] = useState(false)

  useEffect(() => {
    const element = ref.current

    if (
      !element ||
      typeof IntersectionObserver === 'undefined' ||
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    ) {
      setShown(true)
      return
    }

    // Already on screen at mount — above the fold, or restored by a back navigation. Revealing it
    // immediately avoids a pointless animation on content the user is already looking at.
    if (element.getBoundingClientRect().top < window.innerHeight) {
      setShown(true)
      return
    }

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          setShown(true)
          observer.disconnect()
        }
      },
      // Fires slightly before the element reaches the viewport edge, so the motion finishes as it
      // arrives rather than starting once it is already in view.
      { rootMargin: '0px 0px -12% 0px', threshold: 0.01 },
    )

    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  return {
    ref,
    /**
     * Spread onto the element. `data-reveal` is what the reduced-motion override in `theme.css`
     * targets, and the inline style is what keeps the hidden state out of the stylesheet.
     */
    revealProps: {
      'data-reveal': shown ? 'shown' : 'hidden',
      style: {
        opacity: shown ? 1 : 0,
        transform: shown ? 'none' : 'translate3d(0, 14px, 0)',
        transition: 'opacity 0.45s var(--ease-out-soft), transform 0.45s var(--ease-out-soft)',
        // Hints the compositor before the transition starts, then releases it once settled —
        // a permanent `will-change` on many elements costs memory for no benefit.
        willChange: shown ? undefined : ('opacity, transform' as const),
      },
    },
  } as const
}
