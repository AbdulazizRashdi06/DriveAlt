import { afterEach, expect, it, vi } from 'vitest'
import { installChunkRecovery } from './chunkRecovery'

afterEach(() => { sessionStorage.clear(); vi.restoreAllMocks() })

it('reloads once for an outdated build and lets repeated failures reach the error screen', () => {
  const reload = vi.fn()
  const dispose = installChunkRecovery('old-build', reload)
  try {
    const first = new Event('vite:preloadError', { cancelable: true })
    window.dispatchEvent(first)
    expect(first.defaultPrevented).toBe(true)
    const retry = new Event('vite:preloadError', { cancelable: true })
    window.dispatchEvent(retry)
    expect(retry.defaultPrevented).toBe(false)
    expect(reload).toHaveBeenCalledTimes(1)
  } finally { dispose() }
})

it('a subsequent build can recover even after an earlier build reloaded', () => {
  sessionStorage.setItem('drivealt:chunk-reload:old-build', '1')
  const reload = vi.fn(), dispose = installChunkRecovery('new-build', reload)
  try { window.dispatchEvent(new Event('vite:preloadError')); expect(reload).toHaveBeenCalledOnce() }
  finally { dispose() }
})

it('does not loop or suppress the error when storage is blocked', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
  const reload = vi.fn(), dispose = installChunkRecovery('build', reload)
  try {
    const event = new Event('vite:preloadError', { cancelable: true })
    window.dispatchEvent(event)
    expect(event.defaultPrevented).toBe(false)
    expect(reload).not.toHaveBeenCalled()
  } finally { dispose() }
})
