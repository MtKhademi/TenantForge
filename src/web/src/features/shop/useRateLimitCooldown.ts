import { useCallback, useEffect, useRef, useState } from 'react'

/**
 * S42 (B046) — the one shared per-action cooldown driver (F053).
 *
 * A page calls `start(seconds)` when ITS action receives a B046 429 (from the
 * shared parser or the dev throttle gate). The hook then:
 *
 * - reports `coolingDown` (the action must be disabled) and `remainingSeconds`
 *   (the visible countdown);
 * - ticks the countdown down once per second and reaches `0` on its own;
 * - NEVER auto-submits: reaching `0` only re-enables the control, the user must
 *   click again;
 * - cleans up its timer on unmount, so unmounting mid-countdown leaves no
 *   leaked interval and no error.
 *
 * One instance per action. Only the triggering action reads its own hook, so a
 * 429 on one action cools down only that action — the rest of the page stays
 * usable and the data the user entered is never touched (the hook owns no data).
 */
export function useRateLimitCooldown() {
  const [remainingSeconds, setRemainingSeconds] = useState(0)
  const intervalRef = useRef<number | null>(null)

  const stop = useCallback(() => {
    if (intervalRef.current !== null) {
      window.clearInterval(intervalRef.current)
      intervalRef.current = null
    }
    setRemainingSeconds(0)
  }, [])

  const start = useCallback(
    (seconds: number) => {
      if (intervalRef.current !== null) {
        window.clearInterval(intervalRef.current)
        intervalRef.current = null
      }
      setRemainingSeconds(seconds)
      intervalRef.current = window.setInterval(() => {
        setRemainingSeconds((prev) => {
          if (prev <= 1) {
            if (intervalRef.current !== null) {
              window.clearInterval(intervalRef.current)
              intervalRef.current = null
            }
            return 0
          }
          return prev - 1
        })
      }, 1000)
    },
    [],
  )

  // A start may have queued a tick; unmount must clear it (no leaked timer).
  useEffect(() => stop, [stop])

  return {
    remainingSeconds,
    coolingDown: remainingSeconds > 0,
    start,
    stop,
  }
}
