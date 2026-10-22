import { useEffect, useRef, useState, type ClipboardEvent, type FormEvent, type KeyboardEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { api, type Me } from '../lib/api'
import { useApp } from '../lib/app'
import { Mascot } from '../ui/Mascot'

const ID_PATTERN = /^\d{2}-\d{4}$/

/** "240161" or "24 0161" → "24-0161" while typing. */
export function formatUniId(raw: string) {
  const digits = raw.replace(/\D/g, '').slice(0, 6)
  return digits.length > 2 ? `${digits.slice(0, 2)}-${digits.slice(2)}` : digits
}

function CodeInput({ value, onChange, onComplete, disabled }: {
  value: string
  onChange: (v: string) => void
  onComplete: (v: string) => void
  disabled?: boolean
}) {
  const refs = useRef<(HTMLInputElement | null)[]>([])
  useEffect(() => { refs.current[0]?.focus() }, [])

  const set = (i: number, digit: string) => {
    const chars = value.padEnd(6, ' ').split('')
    chars[i] = digit || ' '
    const next = chars.join('').replace(/\s+$/, '')
    onChange(next)
    if (digit && i < 5) refs.current[i + 1]?.focus()
    if (next.replace(/\s/g, '').length === 6) onComplete(next.replace(/\s/g, ''))
  }
  const onKey = (i: number, e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Backspace' && !value[i] && i > 0) refs.current[i - 1]?.focus()
    if (e.key === 'ArrowLeft' && i > 0) refs.current[i - 1]?.focus()
    if (e.key === 'ArrowRight' && i < 5) refs.current[i + 1]?.focus()
  }
  const onPaste = (e: ClipboardEvent<HTMLInputElement>) => {
    const digits = e.clipboardData.getData('text').replace(/\D/g, '').slice(0, 6)
    if (!digits) return
    e.preventDefault()
    onChange(digits)
    refs.current[Math.min(digits.length, 5)]?.focus()
    if (digits.length === 6) onComplete(digits)
  }
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(6, minmax(0, 1fr))', gap: 8 }} role="group" aria-label="6-digit code">
      {Array.from({ length: 6 }, (_, i) => (
        <input key={i} ref={el => { refs.current[i] = el }} className="input" inputMode="numeric" autoComplete={i === 0 ? 'one-time-code' : 'off'}
          aria-label={`Digit ${i + 1}`} maxLength={1} disabled={disabled} value={value[i]?.trim() ?? ''}
          style={{ padding: 0, textAlign: 'center', borderRadius: 18, fontSize: 24, fontWeight: 900, minHeight: 60 }}
          onChange={e => set(i, e.target.value.replace(/\D/g, '').slice(-1))} onKeyDown={e => onKey(i, e)} onPaste={onPaste} />
      ))}
    </div>
  )
}

export function Login() {
  const { me, config } = useApp()
  const qc = useQueryClient()
  const nav = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/chat'
  const [uniId, setUniId] = useState('')
  const [stage, setStage] = useState<'id' | 'code'>('id')
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [cooldown, setCooldown] = useState(0)

  useEffect(() => {
    if (cooldown <= 0) return
    const t = window.setTimeout(() => setCooldown(c => c - 1), 1000)
    return () => window.clearTimeout(t)
  }, [cooldown])

  if (me) return <Navigate to={from} replace />

  const requestCode = async (e?: FormEvent) => {
    e?.preventDefault()
    if (!ID_PATTERN.test(uniId)) { setError('Your uni ID looks like 24-0161.'); return }
    setBusy(true); setError(null)
    try {
      const r = await api.post<{ email: string }>('/api/auth/request', { uniId })
      setEmail(r.email); setStage('code'); setCode(''); setCooldown(45)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const verify = async (value: string) => {
    setBusy(true); setError(null)
    try {
      const user = await api.post<Me>('/api/auth/verify', { uniId, code: value })
      qc.setQueryData(['me'], user)
      nav(from, { replace: true })
    } catch (err) {
      setError((err as Error).message)
      setCode('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <section aria-label="About" className="login-hero">
        <div className="disp" style={{ fontSize: 30, lineHeight: 1, position: 'relative' }}>{config.name}</div>
        <Mascot size={300} color="#FFD34D" mood="happy" tilt={8} className="login-mascot" />
        <span className="sticker" style={{ background: '#2F6BFF', color: '#fff', transform: 'rotate(-8deg)' }} data-s="1">past papers</span>
        <span className="sticker" style={{ background: '#FF8CC2', transform: 'rotate(6deg)' }} data-s="2">slides</span>
        <span className="sticker" style={{ background: '#2BC48A', transform: 'rotate(-3deg)' }} data-s="3">quizzes</span>
        <div style={{ position: 'relative', display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 600 }}>
          <h1 className="disp login-h1">Ask for any page. Get the real thing.</h1>
          <p className="login-sub">Your batch's slides, past papers and quizzes, found by a bot that sends screenshots of the real pages and never types out answers.</p>
        </div>
      </section>

      <section aria-label="Sign in" className="login-panel">
        {stage === 'id' ? (
          <form onSubmit={requestCode} style={{ width: '100%', maxWidth: 380, display: 'flex', flexDirection: 'column', gap: 24 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div className="lbl">CS · CYS · AI · Year 2</div>
              <h2 className="disp" style={{ margin: 0, fontSize: 48, lineHeight: 1 }}>Hey there!</h2>
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <label htmlFor="uni" style={{ font: "800 14px/1 'Nunito', sans-serif", paddingLeft: 8 }}>Uni ID</label>
              <div className="input" style={{ display: 'flex', alignItems: 'center', padding: '0 20px', gap: 0 }}>
                <input id="uni" inputMode="numeric" autoComplete="username" placeholder="24-0161" value={uniId} autoFocus
                  onChange={e => setUniId(formatUniId(e.target.value))}
                  style={{ flex: '0 1 90px', minWidth: 0, border: 0, outline: 0, background: 'transparent', font: "800 16px/1.4 'Nunito', sans-serif", color: '#2A1B0F' }} />
                <span style={{ font: "700 16px/1.4 'Nunito', sans-serif", color: '#7A5A3A' }} className="ellipsis">{config.emailDomain}</span>
              </div>
              <button className="candy lg" type="submit" disabled={busy} style={{ width: '100%' }}>{busy ? 'Sending…' : 'Email me a code'}</button>
            </div>
            {error && <div className="err" role="alert">{error}</div>}
            <p style={{ margin: 0, font: "700 14px/1.5 'Nunito', sans-serif", color: '#7A5A3A' }}>
              We'll send a 6-digit code to your uni email. Only students on the batch list can get in. Not on it? Ping your batch rep.
            </p>
          </form>
        ) : (
          <form onSubmit={e => { e.preventDefault(); if (code.length === 6) void verify(code) }}
            style={{ width: '100%', maxWidth: 380, display: 'flex', flexDirection: 'column', gap: 22 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div className="lbl">Check your inbox</div>
              <h2 className="disp" style={{ margin: 0, fontSize: 44, lineHeight: 1 }}>Type the code</h2>
              <p style={{ margin: 0, font: "700 15px/1.5 'Nunito', sans-serif", color: '#5E4329' }}>Sent to <b>{email}</b>. It expires in 10 minutes.</p>
            </div>
            <CodeInput value={code} onChange={setCode} onComplete={v => void verify(v)} disabled={busy} />
            {error && <div className="err" role="alert">{error}</div>}
            <button className="candy lg" type="submit" disabled={busy || code.replace(/\s/g, '').length < 6}>{busy ? 'Checking…' : "Let's go"}</button>
            <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
              <button className="candy c-white sm" type="button" onClick={() => { setStage('id'); setError(null) }}>Change ID</button>
              <button className="candy c-cream sm" type="button" disabled={cooldown > 0 || busy} onClick={() => void requestCode()}>
                {cooldown > 0 ? `Resend in ${cooldown}s` : 'Resend code'}
              </button>
            </div>
          </form>
        )}
      </section>
    </div>
  )
}
