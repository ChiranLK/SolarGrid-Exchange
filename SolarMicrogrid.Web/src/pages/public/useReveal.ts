import { useEffect, type RefObject } from 'react'

/**
 * Fades `[data-reveal]` elements in as they scroll into view. Content stays visible without
 * JavaScript, without IntersectionObserver, and when the visitor prefers reduced motion.
 */
export function useReveal(containerRef: RefObject<HTMLElement | null>) {
  useEffect(() => {
    const container = containerRef.current
    if (!container || typeof IntersectionObserver === 'undefined') return
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return

    const targets = Array.from(container.querySelectorAll<HTMLElement>('[data-reveal]'))
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return
          entry.target.classList.add('is-visible')
          observer.unobserve(entry.target)
        })
      },
      { rootMargin: '0px 0px -8% 0px', threshold: 0.12 },
    )

    container.classList.add('reveal-ready')
    targets.forEach((target) => observer.observe(target))

    return () => {
      observer.disconnect()
      container.classList.remove('reveal-ready')
    }
  }, [containerRef])
}
