import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, count, sendChat, type ChatMessage, type ChatOption, type Conversation, type SendExtra } from '../lib/api'
import { useApp } from '../lib/app'
import { IconClose, IconMenu, IconPlus, IconSend } from '../ui/Icons'
import { Mascot } from '../ui/Mascot'
import { ResultCard } from '../ui/ResultCard'

interface ConvoData { conversation: Conversation; messages: ChatMessage[] }

const EXAMPLES: { text: string; bg: string; fg?: string }[] = [
  { text: 'Past paper questions on queue rear counting in DS', bg: '#2F6BFF', fg: '#FFFFFF' },
  { text: 'Slides on hashing', bg: '#FF8CC2' },
  { text: 'Quiz 3 from last year', bg: '#2BC48A' },
  { text: 'Where did we cover AVL trees?', bg: '#FFD34D' },
  { text: 'SQL quiz questions on joins', bg: '#FFF3D9' },
  { text: 'Probability formulas sheet', bg: '#FF6B2C' },
]

function History({ activeId, onPick }: { activeId?: string; onPick?: () => void }) {
  const chats = useQuery({ queryKey: ['chats'], queryFn: () => api.get<Conversation[]>('/api/chats') })
  return (
    <>
      <Link className="candy" to="/chat" style={{ width: '100%', flexShrink: 0 }} onClick={onPick}><IconPlus />New chat</Link>
      <div className="lbl" style={{ padding: '12px 6px 2px', flexShrink: 0 }}>Your chats</div>
      {/* Only the list scrolls, so a long history can't stretch the page or the chat sheet. */}
      <div className="hist-list">
      {chats.isLoading && [0, 1, 2].map(i => <div key={i} className="skeleton" style={{ height: 46 }} />)}
      {chats.data?.length === 0 && <p style={{ margin: '4px 6px', font: "700 14px/1.4 'Nunito'", color: '#7A5A3A' }}>No chats yet. Ask me something!</p>}
      {chats.data?.map(c => (
        <Link key={c.id} to={`/chat/${c.id}`} className={`hist ${c.id === activeId ? 'on' : ''}`} title={c.title} onClick={onPick}
          aria-current={c.id === activeId ? 'page' : undefined}>
          {c.title}
        </Link>
      ))}
      </div>
    </>
  )
}

function Composer({ big, onSend, busy }: { big?: boolean; onSend: (t: string) => void; busy: boolean }) {
  const [text, setText] = useState('')
  const submit = (e: FormEvent) => {
    e.preventDefault()
    const t = text.trim()
    if (!t || busy) return
    onSend(t)
    setText('')
  }
  return (
    <form onSubmit={submit} style={{ width: '100%' }}>
      <div className="composer" style={big ? { minHeight: 68, paddingLeft: 26 } : undefined}>
        <label htmlFor="ask" className="sr">Ask for material</label>
        <input id="ask" type="text" value={text} onChange={e => setText(e.target.value)} autoComplete="off" enterKeyHint="send"
          placeholder={big ? 'e.g. every past paper question on Big-O' : 'Ask me for slides, past papers, quizzes…'} maxLength={2000} />
        <button className="candy hide-sm" type="submit" disabled={busy || !text.trim()}>Go fetch!</button>
        <button className="candy rnd show-sm" type="submit" aria-label="Send" disabled={busy || !text.trim()}><IconSend size={20} /></button>
      </div>
    </form>
  )
}

function BotSays({ children }: { children: React.ReactNode }) {
  return (
    <div className="bot-row enter">
      <Mascot size={52} mood="happy" cheeks={false} />
      <div className="bot-col">{children}</div>
    </div>
  )
}

export function Chat() {
  const { id } = useParams()
  const nav = useNavigate()
  const qc = useQueryClient()
  const { toast } = useApp()
  const [pending, setPending] = useState<string | null>(null)
  // The message being sent, shown until the server's copy is in the list. Opening a new chat refetches it, and that
  // refetch can land before the server has saved the message, which used to wipe the optimistic copy until the reply.
  const [sent, setSent] = useState<string | null>(null)
  const [drawer, setDrawer] = useState(false)
  const endRef = useRef<HTMLDivElement>(null)
  // The composer unlocks as soon as the bot's message lands, while a clarifying question's searches keep streaming,
  // so sends can overlap: only the latest one may clear the shared state when it ends.
  const latestSend = useRef(0)

  const convo = useQuery({
    queryKey: ['chat', id],
    queryFn: () => api.get<ConvoData>(`/api/chats/${id}`),
    enabled: !!id,
  })
  const messages = id ? convo.data?.messages ?? [] : []

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'end' })
  }, [messages.length, pending])

  const send = async (text: string, extra: SendExtra = {}) => {
    let cid = id
    const seq = ++latestSend.current
    setSent(text)
    try {
      if (!cid) {
        const c = await api.post<Conversation>('/api/chats')
        cid = c.id
        qc.setQueryData<ConvoData>(['chat', cid], { conversation: c, messages: [] })
        nav(`/chat/${cid}`)
      }
      const key = ['chat', cid]
      const optimistic: ChatMessage = { id: `local-${Date.now()}`, role: 'user', text, refused: false, createdAt: new Date().toISOString(), results: [] }
      qc.setQueryData<ConvoData>(key, old => old && { ...old, messages: [...old.messages, optimistic] })
      setPending('Sniffing around…')
      await sendChat(cid, text, {
        onStatus: s => { if (seq === latestSend.current) setPending(s) },
        onMessage: d => {
          qc.setQueryData<ConvoData>(key, old => {
            const list = old?.messages ?? []
            // A refetch may have dropped the optimistic copy; put it back before the reply, which can now stay on screen
            // for a while (a clarifying question keeps streaming), so the order must be right.
            const mine = [...list].reverse().find(m => m.role === 'user')?.text === text
            return { conversation: d.conversation, messages: [...list, ...(mine ? [] : [optimistic]), d.message] }
          })
          if (seq === latestSend.current) setPending(null)
        },
        onOptions: d => qc.setQueryData<ConvoData>(key, old => old && {
          ...old,
          messages: old.messages.map(m => m.id === d.messageId ? { ...m, options: d.options } : m),
        }),
      }, extra)
    } catch (e) {
      toast((e as Error).message)
    } finally {
      if (seq === latestSend.current) {
        setPending(null)
        if (cid) await qc.invalidateQueries({ queryKey: ['chat', cid] })
        setSent(s => s === text ? null : s)
      }
      void qc.invalidateQueries({ queryKey: ['chats'] })
    }
  }

  const title = convo.data?.conversation.title ?? 'New chat'
  const empty = !id || (messages.length === 0 && !pending && !sent && !convo.isLoading)
  const lastUser = [...messages].reverse().find(m => m.role === 'user')
  const showSent = !!sent && lastUser?.text !== sent

  return (
    <div className="body">
      <aside aria-label="Chats" className="side chat-side hide-sm">
        <History activeId={id} />
      </aside>

      {drawer && (
        <>
          <div className="drawer-back" onClick={() => setDrawer(false)} />
          <aside className="drawer" aria-label="Chats">
            <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
              <button className="candy c-white rnd" type="button" aria-label="Close" onClick={() => setDrawer(false)}><IconClose /></button>
            </div>
            <History activeId={id} onPick={() => setDrawer(false)} />
          </aside>
        </>
      )}

      <main className="sheet-main chat-main">
        <div className="chat-mobilebar show-sm-flex">
          <button className="candy c-white rnd" type="button" aria-label="Your chats" onClick={() => setDrawer(true)}><IconMenu size={20} /></button>
          <div className="disp ellipsis" style={{ flex: 1, fontSize: 20, textAlign: 'center' }}>{empty ? 'New chat' : title}</div>
          <Link className="candy c-white rnd" to="/chat" aria-label="New chat"><IconPlus size={20} /></Link>
        </div>

        {empty ? (
          <div className="chat-empty">
            <Mascot size={120} mood="happy" tilt={-6} className="mascot-hello" />
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              <h1 className="disp chat-empty-h">What should I dig up?</h1>
              <p className="chat-empty-p">Ask for anything from the batch drive. I'll send you screenshots of the real pages.</p>
            </div>
            <Composer big onSend={t => void send(t)} busy={!!pending} />
            <div className="examples">
              {EXAMPLES.map((ex, i) => (
                <button key={ex.text} className="ex" type="button" onClick={() => void send(ex.text)}
                  style={{ background: ex.bg, color: ex.fg, animation: `pop-in 300ms var(--ease-out) ${i * 45}ms both` }}>
                  {ex.text}
                </button>
              ))}
            </div>
          </div>
        ) : (
          <>
            <div className="hide-sm" style={{ padding: '28px 0 20px' }}>
              <h1 className="disp ellipsis" style={{ margin: 0, fontSize: 34, lineHeight: 1.05 }} title={title}>{title}</h1>
            </div>
            <div className="thread" aria-live="polite">
              {convo.isLoading && !sent && <div className="skeleton" style={{ height: 120 }} />}
              {messages.map((m, mi) => m.role === 'user' ? (
                <div key={m.id} className="bubble-user enter">{m.text}</div>
              ) : (
                <BotSays key={m.id}>
                  <div className="bubble-bot">{m.text}{m.results.length > 1 && !m.refused && <ResultCount n={m.results.length} />}</div>
                  {m.options && m.options.length > 0 && (
                    <Options options={m.options} live={mi === messages.length - 1 && !showSent} busy={!!pending}
                      onPick={(o, i) => void send(o.label, { option: i })} />
                  )}
                  {m.results.length > 0 && (
                    <div className="results">
                      {m.results.map((r, i) => <ResultCard key={r.ref} card={r} index={i} all={m.results} />)}
                    </div>
                  )}
                  {!!m.moreCount && mi === messages.length - 1 && !showSent && (
                    <div className="chips">
                      <button className="candy sm c-white" type="button" disabled={!!pending} onClick={() => void send('Show more', { more: true })}>
                        Show more <span className="chip-n">{m.moreCount} waiting</span>
                      </button>
                    </div>
                  )}
                </BotSays>
              ))}
              {showSent && <div className="bubble-user enter">{sent}</div>}
              {pending && (
                <BotSays>
                  <div className="bubble-bot" style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                    <span className="typing" aria-hidden="true"><i /><i /><i /></span>
                    <span style={{ color: '#5E4329' }}>{pending}</span>
                  </div>
                </BotSays>
              )}
              <div ref={endRef} />
            </div>
            <div className="composer-dock">
              <Composer onSend={t => void send(t)} busy={!!pending} />
            </div>
          </>
        )}
      </main>
    </div>
  )
}

/** The choices of a clarifying question, each with its search's state; only the latest question's chips can be picked. */
function Options({ options, live, busy, onPick }: { options: ChatOption[]; live: boolean; busy: boolean; onPick: (o: ChatOption, i: number) => void }) {
  return (
    <div className="chips" role="group" aria-label="Pick one">
      {options.map((o, i) => (
        <button key={i} className="candy sm c-white" type="button" disabled={!live || busy} onClick={() => onPick(o, i)}>
          {o.label}
          {o.count === null
            ? <span className="typing chip-n" aria-label="still searching"><i /><i /><i /></span>
            : <span className="chip-n">{o.count === 0 ? 'none yet' : `${o.count} found`}</span>}
        </button>
      ))}
    </div>
  )
}

function ResultCount({ n }: { n: number }) {
  return <span className="sr"> {count(n, 'result')} below.</span>
}
