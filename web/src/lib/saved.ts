import { useCallback, useSyncExternalStore } from 'react'
import { api } from './api'

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
