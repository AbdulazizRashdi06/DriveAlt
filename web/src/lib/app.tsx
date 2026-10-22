import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
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
      {viewer && <Viewer items={viewer.items} start={viewer.index} onClose={() => setViewer(null)} />}
      {toastText && <div className="toast" role="status">{toastText}</div>}
    </Ctx.Provider>
  )
}

export function useApp() {
  const c = useContext(Ctx)
  if (!c) throw new Error('useApp outside AppProvider')
  return c
}
