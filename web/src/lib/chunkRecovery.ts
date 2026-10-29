/** Recover a stale Vite module URL once per app build, without looping on a persistent network failure. */
export function installChunkRecovery(buildId: string, reload = () => window.location.reload()) {
  const handler = (event: Event) => {
    try {
      const key = `drivealt:chunk-reload:${buildId}`
      if (sessionStorage.getItem(key)) return
      sessionStorage.setItem(key, '1')
    } catch { return } // Storage unavailable: let the route's reload screen handle it.
    event.preventDefault()
    reload()
  }
  window.addEventListener('vite:preloadError', handler)
  return () => window.removeEventListener('vite:preloadError', handler)
}
