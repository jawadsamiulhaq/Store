import { useEffect, useRef, useState } from 'react'

/**
 * Counts a number up to its target once, on mount or whenever the target changes.
 *
 * Kept deliberately short. A dashboard figure is there to be read, and an admin opening it at
 * 9am wants today's revenue, not a performance — so this is long enough to register as motion
 * and over before it can become an obstacle.
 *
 * Driven by `requestAnimationFrame` against a timestamp rather than by a fixed per-frame step,
 * so it takes the same wall-clock time on a 60Hz laptop and a 120Hz phone, and a dropped frame
 * costs smoothness rather than accuracy. The final frame is assigned the exact target, so the
 * displayed figure can never be a rounding artefact of the easing curve.
 */
export function useCountUp(target: number, durationMs = 600): number {
  const [value, setValue] = useState(target)
  const frame = useRef(0)

  useEffect(() => {
    if (
      !Number.isFinite(target) ||
      typeof requestAnimationFrame === 'undefined' ||
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    ) {
      setValue(target)
      return
    }

    const from = 0
    const start = performance.now()

    function step(now: number) {
      const progress = Math.min(1, (now - start) / durationMs)

      // easeOutCubic: fast at the start, settling at the end. Matches the --ease-out-soft feel
      // used everywhere else, so the numbers do not run on a different clock from the tiles.
      const eased = 1 - (1 - progress) ** 3

      setValue(progress === 1 ? target : from + (target - from) * eased)

      if (progress < 1) {
        frame.current = requestAnimationFrame(step)
      }
    }

    frame.current = requestAnimationFrame(step)
    return () => cancelAnimationFrame(frame.current)
  }, [target, durationMs])

  return value
}
