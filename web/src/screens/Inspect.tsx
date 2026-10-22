import { useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, count, TYPE_SINGULAR, type FileDto, type InspectPage, type RegionDto } from '../lib/api'
import { useApp } from '../lib/app'
import { fromCorners, isUsable, moveBox, relPoint, resizeBox, type Handle, type NBox } from '../lib/boxes'
import { FileBadge, fileMeta } from './Browse'
import { IconClose, IconExternal, IconFrame, IconLeft, IconPlus, IconPointer, IconRefresh, IconRight, IconSearch, IconTrash } from '../ui/Icons'
import { Mascot } from '../ui/Mascot'

type Label = RegionDto['label']
interface Draft extends NBox { key: string; id: string | null; label: Label; questionNumber: string | null; text: string; cropUrl: string | null }

const LABELS: { value: Label; text: string }[] = [
  { value: 'question', text: 'Question' },
  { value: 'answer', text: 'Answer (hidden)' },
  { value: 'other', text: 'Other' },
]
const KINDS = ['slide', 'assessment', 'notes', 'other']

const toDrafts = (regions: RegionDto[]): Draft[] =>
  regions.map(r => ({ key: r.id, id: r.id, label: r.label, x: r.x, y: r.y, w: r.w, h: r.h, questionNumber: r.questionNumber, text: r.text, cropUrl: r.cropUrl }))

type Drag =
  | { kind: 'move'; key: string; start: { x: number; y: number }; box: NBox }
  | { kind: 'resize'; key: string; handle: Handle }
  | { kind: 'draw'; start: { x: number; y: number }; current: { x: number; y: number } }

function regionTitle(r: Draft, i: number) {
  const base = r.label === 'question' ? `Question${r.questionNumber ? ` · ${r.questionNumber}` : ''}` : r.label === 'answer' ? 'Answer · hidden' : 'Other'
  return `${i + 1}. ${base}`
}

/** One page: original next to what the AI understood, with an editable region overlay. */
function PageEditor({ page, onSaved }: { page: InspectPage; onSaved: (p: InspectPage) => void }) {
  const { toast } = useApp()
  const [regions, setRegions] = useState<Draft[]>(() => toDrafts(page.regions))
  const [text, setText] = useState(page.text)
  const [tags, setTags] = useState(page.tags)
  const [kind, setKind] = useState(page.kind)
  const [topic, setTopic] = useState(page.topic ?? '')
  const [tagDraft, setTagDraft] = useState('')
  const [selected, setSelected] = useState<string | null>(null)
  const [tool, setTool] = useState<'select' | 'draw'>('select')
  const [drag, setDrag] = useState<Drag | null>(null)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const fig = useRef<HTMLElement>(null)
  const counter = useRef(0)

  const sel = regions.find(r => r.key === selected) ?? null
  const selIndex = sel ? regions.indexOf(sel) : -1
  const edit = (fn: (rs: Draft[]) => Draft[]) => { setRegions(fn); setDirty(true) }
  const update = (key: string, patch: Partial<Draft>) => edit(rs => rs.map(r => (r.key === key ? { ...r, ...patch } : r)))
  const remove = (key: string) => { edit(rs => rs.filter(r => r.key !== key)); setSelected(null) }

  const onPointerDown = (e: ReactPointerEvent) => {
    if (!fig.current || e.button !== 0) return
    const p = relPoint(fig.current, e.clientX, e.clientY)
    const target = e.target as HTMLElement
    const handle = target.dataset.handle as Handle | undefined
    const regKey = target.closest<HTMLElement>('[data-key]')?.dataset.key
    fig.current.setPointerCapture(e.pointerId)
    if (tool === 'draw') { setDrag({ kind: 'draw', start: p, current: p }); setSelected(null); return }
    if (handle && regKey) { setDrag({ kind: 'resize', key: regKey, handle }); return }
    if (regKey) {
      setSelected(regKey)
      const r = regions.find(x => x.key === regKey)!
      setDrag({ kind: 'move', key: regKey, start: p, box: { x: r.x, y: r.y, w: r.w, h: r.h } })
      return
    }
    setSelected(null)
  }
  const onPointerMove = (e: ReactPointerEvent) => {
    if (!drag || !fig.current) return
    const p = relPoint(fig.current, e.clientX, e.clientY)
    if (drag.kind === 'draw') setDrag({ ...drag, current: p })
    else if (drag.kind === 'move') update(drag.key, moveBox(drag.box, p.x - drag.start.x, p.y - drag.start.y))
    else {
      const r = regions.find(x => x.key === drag.key)
      if (r) update(drag.key, resizeBox(r, drag.handle, p.x, p.y))
    }
  }
  const onPointerUp = () => {
    if (drag?.kind === 'draw') {
      const b = fromCorners(drag.start.x, drag.start.y, drag.current.x, drag.current.y)
      if (isUsable(b)) {
        const key = `new-${counter.current++}`
        edit(rs => [...rs, { key, id: null, label: 'question', questionNumber: null, text: '', cropUrl: null, ...b }])
        setSelected(key)
        setTool('select')
      }
    }
    setDrag(null)
  }
  const onKeyDown = (e: ReactKeyboardEvent) => {
    if ((e.key === 'Delete' || e.key === 'Backspace') && sel && (e.target as HTMLElement).tagName !== 'INPUT' && (e.target as HTMLElement).tagName !== 'TEXTAREA') {
      e.preventDefault()
      remove(sel.key)
    }
  }

  const addTag = () => {
    const t = tagDraft.trim().toLowerCase()
    if (t && !tags.includes(t)) { setTags([...tags, t]); setDirty(true) }
    setTagDraft('')
  }

  const save = async () => {
    setSaving(true)
    try {
      const updated = await api.put<InspectPage>(`/api/inspect/pages/${page.id}`, {
        kind, text, topic, tags,
        regions: regions.map(r => ({ id: r.id, label: r.label, x: r.x, y: r.y, w: r.w, h: r.h, questionNumber: r.questionNumber, text: r.text })),
      })
      onSaved(updated)
      setRegions(toDrafts(updated.regions))
      setSelected(null)
      setDirty(false)
      toast('Saved. Crops and search are updated.')
    } catch (e) {
      toast((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const draftBox = drag?.kind === 'draw' ? fromCorners(drag.start.x, drag.start.y, drag.current.x, drag.current.y) : null
  const pct = (v: number) => `${v * 100}%`


  return (
    <div className="compare" onKeyDown={onKeyDown}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12, minWidth: 0 }}>
        <div role="toolbar" aria-label="Region tools" className="toolbar">
          <button className={`tool ${tool === 'select' ? 'on' : ''}`} type="button" aria-pressed={tool === 'select'} onClick={() => setTool('select')}><IconPointer />Select</button>
          <button className={`tool ${tool === 'draw' ? 'on' : ''}`} type="button" aria-pressed={tool === 'draw'} onClick={() => setTool('draw')}><IconFrame />Draw region</button>
          <span aria-hidden="true" style={{ width: 2, height: 24, background: '#F3E6CB', margin: '0 6px' }} />
          <span className="legend">
            <span><i style={{ background: '#2F6BFF' }} />Question</span>
            <span><i style={{ background: '#2A1B0F' }} />Answer (hidden)</span>
            <span><i style={{ border: '2px dashed #9A7B57' }} />Other</span>
          </span>
        </div>
        <div className="stage">
          <figure ref={fig} className={`page-fig ${tool === 'draw' ? 'drawing' : ''}`} style={{ aspectRatio: `${page.width} / ${page.height}`, width: page.width > page.height ? '100%' : 'min(100%, 620px)' }}
            onPointerDown={onPointerDown} onPointerMove={onPointerMove} onPointerUp={onPointerUp} onPointerCancel={() => setDrag(null)}>
            <img src={page.originalUrl} alt={`Original page ${page.number} with detected regions`} width={page.width} height={page.height} draggable={false} />
            {regions.map((r, i) => (
              <div key={r.key} data-key={r.key} className={`reg r-${r.label} ${r.key === selected ? 'sel' : ''}`}
                style={{ left: pct(r.x), top: pct(r.y), width: pct(r.w), height: pct(r.h) }}
                role="button" tabIndex={0} aria-label={regionTitle(r, i)} aria-pressed={r.key === selected}
                onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setSelected(r.key) } }}>
                <span>{r.label === 'question' ? `Question${r.questionNumber ? ` · ${r.questionNumber}` : ''}` : r.label === 'answer' ? 'Answer · hidden' : 'Other'}</span>
                {r.key === selected && (['nw', 'ne', 'sw', 'se'] as Handle[]).map(h => <i key={h} className={`hdl ${h}`} data-handle={h} />)}
              </div>
            ))}
            {draftBox && <div className="draft" style={{ left: pct(draftBox.x), top: pct(draftBox.y), width: pct(draftBox.w), height: pct(draftBox.h) }} />}
          </figure>
        </div>
      </div>

      <section aria-label="What the AI understood" style={{ display: 'flex', flexDirection: 'column', gap: 16, minWidth: 0 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span className="lbl">AI's understanding</span>
          <div className="ai-box">{page.aiSummary || 'No summary.'}{page.error && <><br /><b style={{ color: '#9B2C5E' }}>AI hiccup: {page.error}</b></>}</div>
        </div>

        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <span className="field">
            <label className="lbl" htmlFor="kind">Page kind</label>
            <select id="kind" className="sel" value={kind} onChange={e => { setKind(e.target.value); setDirty(true) }}>
              {KINDS.map(k => <option key={k} value={k}>{k[0].toUpperCase() + k.slice(1)}</option>)}
            </select>
          </span>
          <span className="field" style={{ flex: 1, minWidth: 160 }}>
            <label className="lbl" htmlFor="topic">Topic</label>
            <input id="topic" className="sel" style={{ backgroundImage: 'none', width: '100%' }} value={topic} onChange={e => { setTopic(e.target.value); setDirty(true) }} />
          </span>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <span className="lbl">Tags</span>
          <div className="chip-row" style={{ flexWrap: 'wrap' }}>
            {tags.map(t => (
              <span key={t} className="tg-edit">{t}
                <button type="button" aria-label={`Remove tag ${t}`} onClick={() => { setTags(tags.filter(x => x !== t)); setDirty(true) }}><IconClose size={14} /></button>
              </span>
            ))}
            <label className="sr" htmlFor="addtag">Add tag</label>
            <input id="addtag" className="tag-input" placeholder="+ add tag" value={tagDraft} onChange={e => setTagDraft(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); addTag() } }} onBlur={addTag} />
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 10, padding: 16, borderRadius: 24, background: '#FFF9EC' }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
            <h3 className="disp" style={{ margin: 0, fontSize: 22 }}>{sel ? `Region ${selIndex + 1}` : 'Regions'}</h3>
            <span className="lbl">{sel ? `${selIndex + 1} of ${regions.length}` : count(regions.length, 'region')}</span>
          </div>
          {sel ? (
            <>
              <div role="radiogroup" aria-label="Region label" className="segs blue" style={{ alignSelf: 'stretch', background: '#fff' }}>
                {LABELS.map(l => (
                  <button key={l.value} className={`seg ${sel.label === l.value ? 'on' : ''}`} type="button" role="radio" aria-checked={sel.label === l.value}
                    style={{ flex: 1, minHeight: 42, fontSize: 13, padding: '0 10px' }} onClick={() => update(sel.key, { label: l.value })}>{l.text}</button>
                ))}
              </div>
              {sel.label === 'question' && (
                <span className="field">
                  <label className="lbl" htmlFor="qn">Question number</label>
                  <input id="qn" className="sel" style={{ backgroundImage: 'none', minWidth: 0, width: 160 }} placeholder="e.g. 2(b)" value={sel.questionNumber ?? ''}
                    onChange={e => update(sel.key, { questionNumber: e.target.value || null })} />
                </span>
              )}
              {sel.cropUrl && sel.label === 'question' && !dirty && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  <span className="lbl">Crop students see</span>
                  <div style={{ borderRadius: 18, overflow: 'hidden', border: '3px solid #F3E6CB', lineHeight: 0, background: '#fff' }}>
                    <img src={sel.cropUrl} alt="Crop students will see" style={{ width: '100%', height: 'auto', maxHeight: 260, objectFit: 'contain' }} />
                  </div>
                </div>
              )}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                <label className="lbl" htmlFor="rtext">Region text · {sel.label === 'answer' ? 'never shown or searched' : 'used for search only'}</label>
                <textarea id="rtext" className="textarea" rows={4} value={sel.text} onChange={e => update(sel.key, { text: e.target.value })} />
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <button className="candy c-cream sm" type="button" onClick={() => setTool('draw')}><IconPlus size={16} />Add region</button>
                <button className="candy c-cream sm" type="button" onClick={() => remove(sel.key)}><IconTrash size={16} />Delete</button>
              </div>
            </>
          ) : (
            <>
              <p style={{ margin: 0, font: "700 14px/1.5 'Nunito', sans-serif", color: '#5E4329' }}>
                Click a box to relabel, move or resize it. Use <b>Draw region</b> to add one; Delete removes the selected box.
              </p>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                {regions.map((r, i) => (
                  <button key={r.key} className="file" type="button" style={{ padding: '10px 12px', background: '#fff' }} onClick={() => setSelected(r.key)}>
                    <i style={{ width: 12, height: 12, borderRadius: 4, flex: 'none', background: r.label === 'question' ? '#2F6BFF' : r.label === 'answer' ? '#2A1B0F' : 'transparent', border: r.label === 'other' ? '2px dashed #9A7B57' : 0 }} />
                    <span className="meta"><b>{regionTitle(r, i)}</b>{r.text && <span className="ellipsis">{r.text.slice(0, 90)}</span>}</span>
                  </button>
                ))}
              </div>
            </>
          )}
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <label className="lbl" htmlFor="ptext">Page text the AI read · used for search only, never shown to students</label>
          <textarea id="ptext" className="textarea" rows={8} value={text} onChange={e => { setText(e.target.value); setDirty(true) }} />
        </div>

        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center', position: 'sticky', bottom: 12 }}>
          <button className="candy c-green" type="button" disabled={!dirty || saving} onClick={() => void save()}>{saving ? 'Saving…' : dirty ? 'Save fixes' : 'No changes'}</button>
          {dirty && <button className="candy c-white" type="button" onClick={() => { setRegions(toDrafts(page.regions)); setText(page.text); setTags(page.tags); setKind(page.kind); setTopic(page.topic ?? ''); setDirty(false); setSelected(null) }}>Undo</button>}
        </div>
      </section>
    </div>
  )
}

function InspectFile({ id }: { id: string }) {
  const qc = useQueryClient()
  const { toast } = useApp()
  const [params, setParams] = useSearchParams()
  const q = useQuery({ queryKey: ['inspect', id], queryFn: () => api.get<{ file: FileDto; pages: InspectPage[] }>(`/api/inspect/files/${id}`) })
  const pages = q.data?.pages ?? []
  const current = Math.max(1, Number(params.get('page') ?? 1) || 1)
  const index = Math.max(0, pages.findIndex(p => p.number === current))
  const page = pages[index]
  const go = (n: number) => setParams(p => { const x = new URLSearchParams(p); x.set('page', String(n)); return x }, { replace: true })

  if (q.isLoading) return <div className="skeleton" style={{ height: 480 }} />
  if (!q.data) return <div className="empty"><Mascot size={84} />Couldn't load that file.</div>
  const { file } = q.data

  const rerun = async () => {
    if (!confirm('Re-run the AI on every page? Your manual fixes on this file will be replaced.')) return
    try {
      await api.post(`/api/files/${file.id}/reprocess`)
      toast('Re-reading the whole file. Watch it in Upload.')
      void qc.invalidateQueries({ queryKey: ['inspect', id] })
    } catch (e) { toast((e as Error).message) }
  }

  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
          <nav className="lbl"><Link to="/inspect" style={{ color: '#7A5A3A' }}>Inspect</Link> › {file.courseName} · {TYPE_SINGULAR[file.materialType]}</nav>
          <h1 className="disp ellipsis" style={{ margin: 0, fontSize: 32, lineHeight: 1.05 }} title={file.title}>{file.title}</h1>
          <span style={{ font: "800 14px/1.2 'Nunito', sans-serif", color: '#7A5A3A' }}>Check what I cut out, and fix anything I got wrong.</span>
        </div>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
          <Link className="candy c-white sm" to={`/files/${file.id}?page=${current}`}><IconExternal size={16} />Open original</Link>
          <button className="candy c-cream sm" type="button" onClick={() => void rerun()}><IconRefresh size={16} />Re-run AI</button>
        </div>
      </div>

      {file.status !== 'ready' && <div className="err" style={{ background: '#FFF3C4', color: '#5E4329' }}>This file is still {file.status}. Pages appear here as they're read.</div>}
      {page ? (
        <PageEditor key={page.id + (page.editedAt ?? '')} page={page}
          onSaved={p => qc.setQueryData<{ file: FileDto; pages: InspectPage[] }>(['inspect', id], old => old && { ...old, pages: old.pages.map(x => (x.id === p.id ? p : x)) })} />
      ) : (
        <div className="empty"><Mascot size={84} />No pages read yet.</div>
      )}

      {pages.length > 1 && (
        <footer className="inspect-foot">
          <button className="candy c-white rnd sm" type="button" aria-label="Previous page" disabled={index === 0} onClick={() => go(pages[index - 1].number)}><IconLeft /></button>
          <nav aria-label="Pages" style={{ display: 'flex', gap: 6, flexWrap: 'wrap', flex: 1, justifyContent: 'center' }}>
            {pages.map(p => (
              <button key={p.id} className={`pdot ${p.number === page?.number ? 'on' : p.error ? 'warn' : ''}`} type="button"
                aria-label={`Page ${p.number}${p.error ? ', read without AI' : ''}`} aria-current={p.number === page?.number ? 'page' : undefined} onClick={() => go(p.number)}>
                {p.number}
              </button>
            ))}
          </nav>
          <button className="candy c-white rnd sm" type="button" aria-label="Next page" disabled={index === pages.length - 1} onClick={() => go(pages[index + 1].number)}><IconRight /></button>
        </footer>
      )}
    </>
  )
}

function InspectList() {
  const [filter, setFilter] = useState('')
  const q = useQuery({ queryKey: ['uploads', 'all'], queryFn: () => api.get<FileDto[]>('/api/uploads?all=true') })
  const list = useMemo(() => {
    const f = filter.trim().toLowerCase()
    return (q.data ?? []).filter(x => x.status === 'ready' || x.pageCount > 0)
      .filter(x => !f || `${x.title} ${x.courseName} ${x.originalName}`.toLowerCase().includes(f))
  }, [q.data, filter])
  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <h1 className="disp" style={{ margin: 0, fontSize: 44, lineHeight: 1 }}>Inspect</h1>
          <span style={{ font: "800 16px/1.4 'Nunito', sans-serif", color: '#7A5A3A' }}>Compare any file with what the AI read, and fix tags, text and regions.</span>
        </div>
        <div className="search" style={{ flex: '0 1 360px', minWidth: 240 }}>
          <IconSearch size={20} />
          <label htmlFor="if" className="sr">Filter files</label>
          <input id="if" type="search" placeholder="Filter files" value={filter} onChange={e => setFilter(e.target.value)} />
        </div>
      </div>
      {q.isLoading && [0, 1, 2].map(i => <div key={i} className="skeleton" style={{ height: 64 }} />)}
      {q.data && list.length === 0 && <div className="empty"><Mascot size={84} />Nothing to inspect yet. <Link className="candy" to="/upload">Upload something</Link></div>}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(min(320px, 100%), 1fr))', gap: 10 }}>
        {list.map((f, i) => (
          <Link key={f.id} to={`/inspect/${f.id}`} className="file" style={{ animation: `pop-in 300ms var(--ease-out) ${Math.min(i, 10) * 40}ms both` }}>
            <FileBadge file={f} />
            <span className="meta"><b title={f.title}>{f.title}</b><span>{[f.courseName, fileMeta(f)].filter(Boolean).join(' · ')}</span></span>
            {f.error && <span className="badge" title={f.error}>!</span>}
          </Link>
        ))}
      </div>
    </>
  )
}

export function Inspect() {
  const { id } = useParams()
  useEffect(() => { window.scrollTo({ top: 0 }) }, [id])
  return <main className="page-main">{id ? <InspectFile id={id} /> : <InspectList />}</main>
}
