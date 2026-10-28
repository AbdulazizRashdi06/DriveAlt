import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { Link, NavLink, Navigate, Outlet, useLocation } from 'react-router'
import { useApp } from '../lib/app'
import { IconBookmark, IconChat, IconCheck, IconGrid, IconUpload } from './Icons'
import { Mascot } from './Mascot'

interface TabDef { to: string; label: string; icon: ReactNode; uploader?: boolean }

const TABS: TabDef[] = [
  { to: '/chat', label: 'Chat', icon: <IconChat size={20} /> },
  { to: '/browse', label: 'Browse', icon: <IconGrid size={20} /> },
  { to: '/saved', label: 'Saved', icon: <IconBookmark size={20} /> },
  { to: '/upload', label: 'Upload', icon: <IconUpload size={20} />, uploader: true },
  { to: '/inspect', label: 'Inspect', icon: <IconCheck size={20} />, uploader: true },
]

/** Desktop tab pill with an ink indicator that slides to the active tab. */
function Tabs({ tabs }: { tabs: TabDef[] }) {
  const { pathname } = useLocation()
  const nav = useRef<HTMLElement>(null)
  const [ink, setInk] = useState<{ x: number; w: number } | null>(null)
  const first = useRef(true)

  useLayoutEffect(() => {
    const measure = () => {
      const el = nav.current?.querySelector<HTMLElement>('.tab.on')
      if (!el || !nav.current) { setInk(null); return }
      setInk({ x: el.offsetLeft, w: el.offsetWidth })
    }
    measure()
    window.addEventListener('resize', measure)
    return () => window.removeEventListener('resize', measure)
  }, [pathname, tabs.length])

  useEffect(() => { if (ink) first.current = false }, [ink])

  return (
    <nav ref={nav} className="tabs" aria-label="Main">
      {ink && (
        <span className="tab-ink" aria-hidden="true"
          style={{ width: ink.w, transform: `translateX(${ink.x}px)`, left: 0, transition: first.current ? 'none' : undefined }} />
      )}
      {tabs.map(t => (
        <NavLink key={t.to} to={t.to} className={({ isActive }) => `tab ${isActive ? 'on' : ''}`}>{t.label}</NavLink>
      ))}
    </nav>
  )
}

function AvatarMenu() {
  const { me, signOut } = useApp()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', esc)
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', esc) }
  }, [open])
  if (!me) return null
  return (
    <div ref={ref} style={{ position: 'relative' }}>
      <button className="avatar" type="button" aria-haspopup="menu" aria-expanded={open} aria-label="Account" onClick={() => setOpen(o => !o)}>
        {me.initials}
      </button>
      {open && (
        <div className="menu" role="menu">
          <div className="lbl">{me.uniId} · {me.role === 'uploader' ? 'Uploader' : 'Student'}</div>
          {me.role === 'uploader' && <Link role="menuitem" to="/admin" onClick={() => setOpen(false)}>People & reports</Link>}
          <button role="menuitem" type="button" onClick={() => void signOut()}>Sign out</button>
        </div>
      )}
    </div>
  )
}

export function Layout() {
  const { me, loading, config } = useApp()
  const location = useLocation()
  if (loading) return <div className="empty" style={{ minHeight: '100dvh', justifyContent: 'center' }}><Mascot size={84} /></div>
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />

  const tabs = TABS.filter(t => !t.uploader || me.role === 'uploader')
  return (
    <div className="shell">
      <header className="topbar">
        <Link to="/chat" className="brand" aria-label={`${config.name} home`}>
          <Mascot size={48} />
          <span className="disp">{config.name}</span>
        </Link>
        <Tabs tabs={tabs} />
        <AvatarMenu />
      </header>
      <Outlet />
      <nav className="bottombar" aria-label="Main">
        {tabs.map(t => (
          <NavLink key={t.to} to={t.to} className={({ isActive }) => `btab ${isActive ? 'on' : ''}`}>
            {t.icon}{t.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}

export function RequireUploader({ children }: { children: ReactNode }) {
  const { me } = useApp()
  if (me?.role !== 'uploader') return <Navigate to="/chat" replace />
  return <>{children}</>
}
