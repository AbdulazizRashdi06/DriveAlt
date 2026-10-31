import { useRef, useState, type ChangeEvent, type DragEvent } from 'react'
import { Link } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, count, formatBytes, TYPE_ORDER, TYPE_SINGULAR, type Course, type FileDto, type FileWait, type UploadCheckItem, type UploadCheckResult } from '../lib/api'
import { useApp } from '../lib/app'
import { FileBadge } from './Browse'
import { IconCamera, IconRefresh, IconTrash } from '../ui/Icons'
import { Mascot } from '../ui/Mascot'

const STEPS = ['Splitting pages', 'Reading', 'Tagging topics', 'Cropping', 'Ready']
const ACCEPT = '.pdf,.pptx,.ppt,.docx,.doc,image/*,.heic,.heif'
const MAX_HASH_BYTES = 64 * 1024 * 1024

export function Pipeline({ file }: { file: FileDto }) {
  if (file.status === 'uploaded') {
    return (
      <div className="steps">
        <div className="bar" aria-hidden="true">{STEPS.map(s => <i key={s} />)}</div>
        <span>Waiting for you: check course & type, then Process.</span>
      </div>
    )
  }
  if (file.status === 'failed') {
    return (
      <div className="steps">
        <div className="bar" aria-hidden="true">{STEPS.map(s => <i key={s} className="fail" />)}</div>
        <span style={{ color: '#9B2C5E' }} title={file.error ?? ''} className="ellipsis">Hit a snag: {file.error ?? 'unknown error'}</span>
      </div>
    )
  }
  const step = file.status === 'queued' ? 0 : file.step
  const label = file.status === 'queued' ? 'Queued…'
    : file.status === 'ready' ? (file.error ? 'Ready (some pages read without AI)' : 'Ready, and searchable')
    : `Step ${step} of 5 · ${STEPS[step - 1]}${file.status === 'reading' && file.pageCount ? ` ${file.progress}/${file.pageCount}` : ''}…`
  const why = file.status !== 'ready' ? waitText(file.wait) : null
  return (
    <div className="steps" role="status" aria-label={why ? `${label} ${why}` : label}>
      <div className="bar" aria-hidden="true">
        {STEPS.map((s, i) => <i key={s} className={file.status === 'ready' || i + 1 < step ? 'done' : i + 1 === step ? 'now' : ''} />)}
      </div>
      <span style={file.status === 'ready' ? { color: '#1E8F64' } : undefined}>{label}</span>
      {why && <span className={`wait-why ${file.wait?.reason ?? ''}`} title={file.wait?.detail ?? undefined}>{why}</span>}
    </div>
  )
}

const clock = (iso?: string | null) => iso ? new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : null

/** The server's reason for an unfinished file, in plain words. */
export function waitText(w?: FileWait | null): string | null {
  if (!w) return null
  switch (w.reason) {
    case 'reading': return 'The AI is reading it now. A batch of up to 8 pages takes a few minutes.'
    case 'queued': return w.until ? `Starts at ${clock(w.until)}.` : !w.ahead ? 'Next in line.' : `Waiting in line: ${count(w.ahead, 'file')} ahead.`
    case 'paused': return /429|limit/i.test(w.detail ?? '')
      ? `Paused: the AI's usage limit was reached. Resumes about ${clock(w.until)}.`
      : `Paused: the AI service isn't available right now. Tries again about ${clock(w.until)}.`
    case 'retrying': return `Hit an error, trying again at ${clock(w.until)} (attempt ${w.attempt ?? 1} of 3)${w.detail ? `: ${w.detail}` : '.'}`
    case 'held': return `On hold${w.detail ? `: ${w.detail.replace(/^Held:?\s*/i, '')}` : ''}. Press Resume to process it now.`
    case 'stuck': return "Not in the processing queue, so it won't move. Press Retry."
  }
}

function CourseSelect({ courses, value, onChange, id }: { courses: Course[]; value: number | null; onChange: (v: number) => void; id: string }) {
  return (
    <select id={id} className="sel" value={value ?? ''} onChange={e => onChange(Number(e.target.value))}>
      {value === null && <option value="" disabled>Choose course…</option>}
      {[1, 2].map(s => (
        <optgroup key={s} label={`Semester ${s}`}>
          {courses.filter(c => c.semester === s).map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
        </optgroup>
      ))}
    </select>
  )
}

function UploadRow({ file, courses, onChange }: { file: FileDto; courses: Course[]; onChange: () => void }) {
  const { toast } = useApp()
  const [busy, setBusy] = useState(false)
  const editable = file.status === 'uploaded' || file.status === 'ready' || file.status === 'failed'
  const patch = async (body: Record<string, unknown>) => {
    try { await api.patch(`/api/files/${file.id}`, body); onChange() } catch (e) { toast((e as Error).message) }
  }
  const act = async (fn: () => Promise<unknown>) => {
    setBusy(true)
    try { await fn(); onChange() } catch (e) { toast((e as Error).message) } finally { setBusy(false) }
  }
  const guessed = (k: string) => file.guessed.includes(k)
  const pagesText = file.pageCount ? (file.sourceKind === 'images' ? count(file.pageCount, 'photo') : count(file.pageCount, 'page')) : file.parts > 1 ? count(file.parts, 'photo') : null

  return (
    <div className="row">
      <FileBadge file={file} />
      <div className="fname">
        <b title={file.title}>{file.title}</b>
        <span>{[pagesText, formatBytes(file.sizeBytes)].filter(Boolean).join(' · ')}</span>
      </div>
      <span className="field">
        <label className="lbl" htmlFor={`c-${file.id}`}>
          Course {file.courseId === null ? <span className="guess pick">Pick one</span> : guessed('course') && <span className="guess">Guessed, check</span>}
        </label>
        {editable ? <CourseSelect id={`c-${file.id}`} courses={courses} value={file.courseId} onChange={v => void patch({ courseId: v })} />
          : <span className="sel" style={{ display: 'inline-flex', alignItems: 'center' }}>{file.courseName}</span>}
      </span>
      <span className="field">
        <label className="lbl" htmlFor={`t-${file.id}`}>Type {guessed('type') && <span className="guess">Guessed, check</span>}</label>
        <select id={`t-${file.id}`} className="sel" value={file.materialType} disabled={!editable} onChange={e => void patch({ materialType: e.target.value })}>
          {TYPE_ORDER.map(t => <option key={t} value={t}>{TYPE_SINGULAR[t]}</option>)}
        </select>
      </span>
      <span className="field" style={{ width: 110 }}>
        <label className="lbl" htmlFor={`y-${file.id}`}>Year {guessed('year') && <span className="guess">AI</span>}</label>
        <input id={`y-${file.id}`} className="sel" style={{ minWidth: 0, width: 110, padding: '0 14px', backgroundImage: 'none' }} inputMode="numeric"
          defaultValue={file.year ?? ''} placeholder="2024" disabled={!editable}
          onBlur={e => { const v = e.target.value.trim(); if (v !== String(file.year ?? '')) void patch(v ? { year: Number(v) } : { clearYear: true }) }} />
      </span>
      <Pipeline file={file} />
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        {file.status === 'uploaded' && (
          <button className="candy c-green sm" type="button" disabled={busy || file.courseId === null}
            onClick={() => void act(() => api.post(`/api/files/${file.id}/process`))}>Process</button>
        )}
        {(file.status === 'ready' || (file.status === 'failed' && file.pageCount > 0)) && <Link className="candy c-white sm" to={`/inspect/${file.id}`}>Inspect</Link>}
        {file.wait?.reason === 'held' && (
          <button className="candy c-white sm" type="button" disabled={busy} onClick={() => void act(() => api.post(`/api/files/${file.id}/resume`))}>
            <IconRefresh size={16} />Resume
          </button>
        )}
        {(file.status === 'failed' || file.wait?.reason === 'stuck') && (
          <button className="candy c-white sm" type="button" disabled={busy} onClick={() => void act(() => api.post(`/api/files/${file.id}/process`))}>
            <IconRefresh size={16} />Retry
          </button>
        )}
        <button className="candy c-cream rnd sm" type="button" aria-label={`Delete ${file.title}`} title="Delete" disabled={busy}
          onClick={() => { if (confirm(`Delete “${file.title}” and everything made from it?`)) void act(() => api.del(`/api/files/${file.id}`)) }}>
          <IconTrash size={16} />
        </button>
      </div>
    </div>
  )
}

function uploadWithProgress(form: FormData, onProgress: (pct: number) => void) {
  return new Promise<{ duplicate: boolean; file: FileDto }[]>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', '/api/uploads')
    xhr.upload.onprogress = e => { if (e.lengthComputable) onProgress(Math.round((e.loaded / e.total) * 100)) }
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) resolve(JSON.parse(xhr.responseText))
      else {
        let msg = `Upload failed (${xhr.status}).`
        try { msg = JSON.parse(xhr.responseText).error ?? msg } catch { /* not JSON */ }
        reject(new Error(msg))
      }
    }
    xhr.onerror = () => reject(new Error('Upload failed. Check your connection.'))
    xhr.send(form)
  })
}

async function sha256Hex(file: File): Promise<string> {
  try {
    if (!globalThis.crypto?.subtle) return ''
    const buf = await file.arrayBuffer()
    const digest = await globalThis.crypto.subtle.digest('SHA-256', buf)
    return Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('')
  } catch {
    return ''
  }
}

export function Upload() {
  const { toast } = useApp()
  const qc = useQueryClient()
  const [over, setOver] = useState(false)
  const [combine, setCombine] = useState(false)
  const [progress, setProgress] = useState<number | null>(null)
  const [exactMatches, setExactMatches] = useState<{ fileName: string; existing: FileDto }[]>([])
  const [pendingSimilar, setPendingSimilar] = useState<{ file: File; existing: FileDto }[]>([])
  const pick = useRef<HTMLInputElement>(null)
  const camera = useRef<HTMLInputElement>(null)

  const courses = useQuery({ queryKey: ['courses'], queryFn: () => api.get<Course[]>('/api/courses') })
  const uploads = useQuery({
    queryKey: ['uploads'],
    queryFn: () => api.get<FileDto[]>('/api/uploads'),
    refetchInterval: q => (q.state.data?.some(f => f.status !== 'ready' && f.status !== 'failed') ? 2000 : false),
  })
  const refresh = () => void qc.invalidateQueries({ queryKey: ['uploads'] })

  const send = async (files: File[], force = false) => {
    if (files.length === 0) return
    let toUpload = files
    if (!force && !(combine && files.length > 1)) {
      try {
        // One file in memory at a time, and big files skip the browser hash: the server still catches exact copies.
        const items: UploadCheckItem[] = []
        for (const f of files)
          items.push({ fileName: f.name, sizeBytes: f.size, sha256: f.size <= MAX_HASH_BYTES ? await sha256Hex(f) : undefined })
        const checks = await api.post<UploadCheckResult[]>('/api/uploads/check', { items })
        const nextExact: { fileName: string; existing: FileDto }[] = []
        const nextSimilar: { file: File; existing: FileDto }[] = []
        const passed: File[] = []
        for (let i = 0; i < files.length; i++) {
          const chk = checks.find(c => c.index === i)
          if (chk?.kind === 'exact' && chk.existing) nextExact.push({ fileName: files[i].name, existing: chk.existing })
          else if (chk?.kind === 'similar' && chk.existing) nextSimilar.push({ file: files[i], existing: chk.existing })
          else passed.push(files[i])
        }
        if (nextExact.length > 0) setExactMatches(prev => [...nextExact, ...prev])
        if (nextSimilar.length > 0) setPendingSimilar(prev => [...nextSimilar, ...prev])
        toUpload = passed
        if (toUpload.length === 0) {
          if (nextExact.length > 0 && nextSimilar.length === 0) toast(`${count(nextExact.length, 'file')} already in the drive.`)
          else if (nextSimilar.length > 0) toast('Check the duplicate warning below before uploading.')
          return
        }
      } catch {
        // If pre-check fails, fall back to normal upload.
      }
    }

    const form = new FormData()
    toUpload.forEach(f => form.append('files', f))
    if (combine && toUpload.length > 1) form.append('combine', 'true')
    setProgress(0)
    try {
      const res = await uploadWithProgress(form, setProgress)
      const dups = res.filter(r => r.duplicate).length
      toast(dups ? `${count(res.length - dups, 'file')} added, ${dups} already in the drive.` : `${count(res.length, 'file')} added. Check the course & type, then Process.`)
      refresh()
    } catch (e) {
      toast((e as Error).message)
    } finally {
      setProgress(null)
    }
  }
  const onPick = (e: ChangeEvent<HTMLInputElement>) => { void send(Array.from(e.target.files ?? [])); e.target.value = '' }
  const onDrop = (e: DragEvent) => { e.preventDefault(); setOver(false); void send(Array.from(e.dataTransfer.files)) }

  // Files still to do (waiting, processing or failed) first; finished ones below. Newest first within each.
  const list = [...(uploads.data ?? [])].sort((a, b) => Number(a.status === 'ready') - Number(b.status === 'ready'))
  const waiting = list.filter(f => f.status === 'uploaded' && f.courseId !== null)
  const active = list.filter(f => f.status !== 'ready' && f.status !== 'failed').length

  const processAll = async () => {
    for (const f of waiting) {
      try { await api.post(`/api/files/${f.id}/process`) } catch (e) { toast((e as Error).message) }
    }
    refresh()
  }

  return (
    <main className="page-main">
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 16, flexWrap: 'wrap' }}>
        <h1 className="disp" style={{ margin: 0, fontSize: 44, lineHeight: 1 }}>Upload</h1>
        <span style={{ font: "800 16px/1.4 'Nunito', sans-serif", color: '#7A5A3A' }}>Feed the drive. Check what I read anytime in Inspect.</span>
      </div>

      <div className={`drop ${over ? 'over' : ''}`} onDragOver={e => { e.preventDefault(); setOver(true) }} onDragLeave={() => setOver(false)} onDrop={onDrop}
        onClick={e => { if (e.target === e.currentTarget) pick.current?.click() }}>
        <Mascot size={110} mood="hungry" className={over ? 'chomp' : undefined} />
        <span style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          <span className="disp" style={{ fontSize: 32, lineHeight: 1 }}>{progress !== null ? `Munching… ${progress}%` : "Drop files here, I'm hungry"}</span>
          <span style={{ font: "700 15px/1.4 'Nunito', sans-serif", color: '#5E4329' }}>PDF, PPTX, DOCX and photos (JPG, PNG, HEIC). Lots at once is fine.</span>
          <span style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginTop: 4 }}>
            <button className="candy" type="button" disabled={progress !== null} onClick={() => pick.current?.click()}>Choose files</button>
            <button className="candy c-white" type="button" disabled={progress !== null} onClick={() => camera.current?.click()}><IconCamera />Take a photo</button>
          </span>
          <label style={{ display: 'inline-flex', alignItems: 'center', gap: 10, font: "800 14px/1.3 'Nunito', sans-serif", cursor: 'pointer' }}>
            <input type="checkbox" checked={combine} onChange={e => setCombine(e.target.checked)} style={{ width: 20, height: 20, accentColor: '#FF6B2C' }} />
            Several photos of one paper? Combine them into one file.
          </label>
        </span>
        <input ref={pick} type="file" multiple accept={ACCEPT} className="sr" onChange={onPick} tabIndex={-1} aria-hidden="true" />
        <input ref={camera} type="file" accept="image/*" capture="environment" className="sr" onChange={onPick} tabIndex={-1} aria-hidden="true" />
      </div>

      {(pendingSimilar.length > 0 || exactMatches.length > 0) && (
        <section aria-label="Duplicate warnings" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {pendingSimilar.map((p, idx) => (
            <div key={`${p.file.name}-${idx}`} className="dup-card similar" role="region" aria-label={`Possible duplicate: ${p.file.name}`}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4, flex: '1 1 260px', minWidth: 0 }}>
                <span className="lbl" style={{ color: '#9B2C5E' }}>Possible duplicate</span>
                <b className="ellipsis">“{p.file.name}” looks like “{p.existing.title}”</b>
                <span style={{ font: "700 13px/1.3 'Nunito', sans-serif", color: '#7A5A3A' }}>
                  {[p.existing.courseName, TYPE_SINGULAR[p.existing.materialType], p.existing.year, p.existing.pageCount ? count(p.existing.pageCount, 'page') : null].filter(Boolean).join(' · ')}
                </span>
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                <Link className="candy c-white sm" to={`/files/${p.existing.id}`}>View existing</Link>
                <button className="candy c-green sm" type="button" onClick={() => {
                  setPendingSimilar(list => list.filter((_, i) => i !== idx))
                  void send([p.file], true)
                }}>Upload anyway</button>
                <button className="candy c-cream sm" type="button" onClick={() => setPendingSimilar(list => list.filter((_, i) => i !== idx))}>Skip</button>
              </div>
            </div>
          ))}
          {exactMatches.map((m, idx) => (
            <div key={`${m.fileName}-${idx}`} className="dup-card exact" role="region" aria-label={`Already in drive: ${m.fileName}`}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4, flex: '1 1 260px', minWidth: 0 }}>
                <span className="lbl" style={{ color: '#1E8F64' }}>Already in the drive · Skipped upload</span>
                <b className="ellipsis">“{m.fileName}” is identical to “{m.existing.title}”</b>
                <span style={{ font: "700 13px/1.3 'Nunito', sans-serif", color: '#7A5A3A' }}>
                  {[m.existing.courseName, TYPE_SINGULAR[m.existing.materialType], m.existing.year].filter(Boolean).join(' · ')}
                </span>
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                <Link className="candy c-white sm" to={`/files/${m.existing.id}`}>Open file</Link>
                {m.existing.status === 'ready' && <Link className="candy c-cream sm" to={`/inspect/${m.existing.id}`}>Inspect</Link>}
                <button className="candy c-cream sm" type="button" onClick={() => setExactMatches(list => list.filter((_, i) => i !== idx))}>Dismiss</button>
              </div>
            </div>
          ))}
        </section>
      )}

      <section aria-labelledby="proc" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
          <h2 id="proc" className="disp" style={{ margin: 0, fontSize: 26 }}>
            {list.length === 0 ? 'Nothing uploaded yet' : active > 0 ? `Working on ${count(active, 'file')}` : 'Your uploads'}
          </h2>
          {waiting.length > 0 && <button className="candy c-green" type="button" onClick={() => void processAll()}>Process {count(waiting.length, 'file')}</button>}
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {list.map(f => <UploadRow key={f.id} file={f} courses={courses.data ?? []} onChange={refresh} />)}
        </div>
        <p className="lbl" style={{ margin: '4px 0 0', textTransform: 'none', letterSpacing: 0, fontWeight: 700 }}>
          Steps: splitting pages → reading → tagging → cropping → ready. Files are searchable as soon as they're ready.
        </p>
      </section>
    </main>
  )
}
