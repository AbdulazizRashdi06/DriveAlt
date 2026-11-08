import { useCallback, useSyncExternalStore } from 'react'
import { api } from './api'

/** What a guest sees on Save: saving needs a uni ID (the server answers 403 for guests). */
export const GUEST_SAVE_TEXT = 'Sign in with your uni ID to save pages and questions.'

// Shared bookmark state so a card, the viewer and the Saved page agree without refetching.
const overrides = new Map<string, boolean>()
const listeners = new Set<() => void>()
let version = 0

function emit() {
  version++
  listeners.forEach(l => l())
}

function subscribe(l: () => void) {
  listeners.add(l)
  return () => listeners.delete(l)
}

export function useSaved(ref: string, initial: boolean) {
  useSyncExternalStore(subscribe, () => version)
  const saved = overrides.has(ref) ? overrides.get(ref)! : initial
  const toggle = useCallback(async () => {
    const next = !(overrides.has(ref) ? overrides.get(ref)! : initial)
    overrides.set(ref, next)
    emit()
    try {
      if (next) await api.post('/api/bookmarks', { ref })
      else await api.del(`/api/bookmarks/${encodeURIComponent(ref)}`)
    } catch (e) {
      overrides.set(ref, !next)
      emit()
      throw e
    }
    return next
  }, [ref, initial])
  return [saved, toggle] as const
}
