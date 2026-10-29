import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, type AppConfig, type Me, type ResultCard } from './api'
import { Viewer } from '../ui/Viewer'

interface AppCtx {
  me: Me | null
  config: AppConfig
  loading: boolean
  refreshMe: () => Promise<void>
  signOut: () => Promise<void>
  toast: (text: string) => void
  openViewer: (items: ResultCard[], index: number) => void
}

const Ctx = createContext<AppCtx | null>(null)

const defaultConfig: AppConfig = { name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: false }

export function AppProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient()
  const meQuery = useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try {
        return (await api.get<Me | undefined>('/api/auth/me')) ?? null
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null
        throw e
      }
    },
    staleTime: 60_000,
  })
  const configQuery = useQuery({ queryKey: ['config'], queryFn: () => api.get<AppConfig>('/api/config'), staleTime: Infinity })

  const [toastText, setToastText] = useState<string | null>(null)
  const toastTimer = useRef<number | undefined>(undefined)
  const toast = useCallback((text: string) => {
    setToastText(text)
    window.clearTimeout(toastTimer.current)
    toastTimer.current = window.setTimeout(() => setToastText(null), 2600)
  }, [])

  const [viewer, setViewer] = useState<{ items: ResultCard[]; index: number } | null>(null)
  const openViewer = useCallback((items: ResultCard[], index: number) => setViewer({ items, index }), [])
  const [params, setParams] = useSearchParams()
  const refParam = params.get('ref')
  const openedRef = useRef<string | null>(null)

  useEffect(() => {
    if (!refParam) { openedRef.current = null; return }
    if (!meQuery.data || openedRef.current === refParam) return
    let cancelled = false
    void api.get<ResultCard[]>(`/api/cards?refs=${encodeURIComponent(refParam)}`).then(cards => {
      if (cancelled) return
      openedRef.current = refParam
      if (cards.length > 0) setViewer({ items: cards, index: 0 })
      else toast("Couldn't find that shared item.")
    }).catch(() => {
      if (cancelled) return
      openedRef.current = refParam
      toast("Couldn't open that shared link.")
    })
    return () => { cancelled = true }
  }, [refParam, meQuery.data, toast])

  const closeViewer = useCallback(() => {
    setViewer(null)
    if (params.has('ref')) {
      const next = new URLSearchParams(params)
      next.delete('ref')
      setParams(next, { replace: true })
    }
  }, [params, setParams])

  const value = useMemo<AppCtx>(() => ({
    me: meQuery.data ?? null,
    config: configQuery.data ?? defaultConfig,
    loading: meQuery.isLoading,
    refreshMe: async () => { await qc.invalidateQueries({ queryKey: ['me'] }) },
    signOut: async () => {
      await api.post('/api/auth/logout')
      qc.clear()
      qc.setQueryData(['me'], null)
    },
    toast,
    openViewer,
  }), [meQuery.data, meQuery.isLoading, configQuery.data, qc, toast, openViewer])

  return (
    <Ctx.Provider value={value}>
      {children}
      {viewer && <Viewer items={viewer.items} start={viewer.index} onClose={closeViewer} />}
      {toastText && <div className="toast" role="status">{toastText}</div>}
    </Ctx.Provider>
  )
}

export function useApp() {
  const c = useContext(Ctx)
  if (!c) throw new Error('useApp outside AppProvider')
  return c
}
