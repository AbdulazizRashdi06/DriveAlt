import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { api, count, TYPE_SINGULAR, type FileDto, type PageRegion, type PageThumb, type ResultCard } from '../lib/api'
import { useApp } from '../lib/app'
import { IconBack, IconClose, IconDownload, IconExpand, IconShare } from '../ui/Icons'
import { Mascot } from '../ui/Mascot'
import { ReportButton } from '../ui/ReportButton'
import { copyShareLink } from '../ui/Viewer'

/** Renders one PDF page when it scrolls near the viewport. */
function PdfPage({ doc, number, width }: { doc: PDFDocumentProxy; number: number; width: number }) {
  const holder = useRef<HTMLDivElement>(null)
  const canvas = useRef<HTMLCanvasElement>(null)
  const [visible, setVisible] = useState(false)
  const [ratio, setRatio] = useState(1.414)

  useEffect(() => {
    let cancelled = false
    void doc.getPage(number).then(p => {
      if (cancelled) return
      const v = p.getViewport({ scale: 1 })
      setRatio(v.height / v.width)
    })
    return () => { cancelled = true }
  }, [doc, number])

  useEffect(() => {
    const el = holder.current
    if (!el) return
    const io = new IntersectionObserver(entries => { if (entries.some(e => e.isIntersecting)) setVisible(true) }, { rootMargin: '800px 0px' })
    io.observe(el)
    return () => io.disconnect()
  }, [])

  useEffect(() => {
    if (!visible || !canvas.current || width <= 0) return
    let task: ReturnType<Awaited<ReturnType<PDFDocumentProxy['getPage']>>['render']> | null = null
    let cancelled = false
    void doc.getPage(number).then(page => {
      if (cancelled || !canvas.current) return
      const dpr = Math.min(window.devicePixelRatio || 1, 2)
      const base = page.getViewport({ scale: 1 })
      const viewport = page.getViewport({ scale: (width / base.width) * dpr })
      const c = canvas.current
      c.width = Math.floor(viewport.width)
      c.height = Math.floor(viewport.height)
      task = page.render({ canvas: c, viewport })
      task.promise.catch(() => { /* cancelled */ })
    })
    return () => { cancelled = true; task?.cancel() }
  }, [visible, doc, number, width])

  return (
    <div>
      <div ref={holder} id={`page-${number}`} className="doc-page" style={{ aspectRatio: `1 / ${ratio}` }}>
        <canvas ref={canvas} aria-label={`Page ${number}`} />
        <span className="doc-num">{number}</span>
      </div>
    </div>
  )
}

/** Only for files with no rendered pages yet (still processing), so there's nothing to report against. */
function PdfDoc({ url, startPage, onPageCount }: { url: string; startPage: number; onPageCount: (n: number) => void }) {
  const [doc, setDoc] = useState<PDFDocumentProxy | null>(null)
  const [error, setError] = useState<string | null>(null)
  const wrap = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)
  const scrolled = useRef(false)

  useEffect(() => {
    let cancelled = false
    let destroy: (() => void) | null = null
    void Promise.all([import('pdfjs-dist'), import('pdfjs-dist/build/pdf.worker.min.mjs?url')]).then(([pdfjs, w]) => {
      if (cancelled) return
      pdfjs.GlobalWorkerOptions.workerSrc = w.default
      const task = pdfjs.getDocument({ url, withCredentials: true })
      destroy = () => { void task.destroy() }
      task.promise.then(d => { if (!cancelled) { setDoc(d); onPageCount(d.numPages) } }, e => { if (!cancelled) setError(String(e?.message ?? e)) })
    })
    return () => { cancelled = true; destroy?.() }
  }, [url, onPageCount])

  useEffect(() => {
    const el = wrap.current
    if (!el) return
    const ro = new ResizeObserver(() => setWidth(Math.min(el.clientWidth, 1100)))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  useEffect(() => {
    if (!doc || scrolled.current || width === 0) return
    scrolled.current = true
    requestAnimationFrame(() => document.getElementById(`page-${startPage}`)?.scrollIntoView({ block: 'start' }))
  }, [doc, width, startPage])

  return (
    <div ref={wrap} className="doc-pages">
      {error && <div className="err">Couldn't open this file: {error}</div>}
      {!doc && !error && <div className="skeleton" style={{ width: '100%', aspectRatio: '1 / 1.41' }} />}
      {doc && width > 0 && Array.from({ length: doc.numPages }, (_, i) => <PdfPage key={i} doc={doc} number={i + 1} width={width} />)}
    </div>
  )
}

function toPageCard(file: FileDto, p: PageThumb): ResultCard {
  const pageUrl = p.pageUrl ?? `/api/files/${file.id}/view?part=${p.number}`
  const thumbUrl = p.thumbUrl ?? pageUrl
  return {
    ref: `p:${p.id}`, kind: p.kind === 'slide' ? 'slide' : 'page', imageUrl: pageUrl, pageImageUrl: pageUrl, thumbUrl,
    fileId: file.id, pageId: p.id, regionId: null, fileTitle: file.title, fileName: file.originalName, sourceKind: file.sourceKind,
    courseSlug: file.courseSlug ?? '', courseName: file.courseName ?? '', courseColor: file.courseColor ?? '#FF6B2C', semester: 0,
    materialType: file.materialType, year: file.year, assessmentLabel: file.assessmentLabel, pageNumber: p.number, pageCount: file.pageCount,
    questionNumber: null, tags: p.tags ?? [], box: null, width: p.width, height: p.height, saved: p.saved ?? false, answerRef: null, answerImageUrl: null,
  }
}

function toQuestionCard(file: FileDto, p: PageThumb, r: PageRegion): ResultCard {
  const pageUrl = p.pageUrl ?? `/api/files/${file.id}/view?part=${p.number}`
  const thumbUrl = p.thumbUrl ?? pageUrl
  const v = pageUrl.includes('?v=') ? `?v=${pageUrl.split('?v=')[1]}` : ''
  return {
    ref: `r:${r.id}`, kind: 'question', imageUrl: `/api/media/crops/${r.id}${v}`, pageImageUrl: pageUrl, thumbUrl,
    fileId: file.id, pageId: p.id, regionId: r.id, fileTitle: file.title, fileName: file.originalName, sourceKind: file.sourceKind,
    courseSlug: file.courseSlug ?? '', courseName: file.courseName ?? '', courseColor: file.courseColor ?? '#FF6B2C', semester: 0,
    materialType: file.materialType, year: file.year, assessmentLabel: file.assessmentLabel, pageNumber: p.number, pageCount: file.pageCount,
    questionNumber: r.questionNumber, tags: p.tags ?? [], box: { x: r.x, y: r.y, w: r.w, h: r.h }, width: p.width, height: p.height, saved: r.saved ?? false,
    answerRef: null, answerImageUrl: null,
  }
}

/** "Open original": the untouched file (it may contain answers — the bot just never repeats them). */
export function FileView() {
  const { id } = useParams()
  // Filters, PDF state and the jump selector belong to one file, including history navigation.
  return <FileReader key={id} />
}

function FileReader() {
  const { id } = useParams()
  const [params] = useSearchParams()
  const nav = useNavigate()
  const { me, openViewer, toast } = useApp()
  const startPage = Math.max(1, Number(params.get('page') ?? 1) || 1)
  const [pageCount, setPageCount] = useState<number | null>(null)
  const [tag, setTag] = useState<string | null>(null)
  const [showQuestions, setShowQuestions] = useState(true)
  const q = useQuery({ queryKey: ['file', id], queryFn: () => api.get<{ file: FileDto; pages: PageThumb[] }>(`/api/files/${id}`) })
  const file = q.data?.file
  const pages = q.data?.pages ?? []

  const tags = useMemo(() => [...new Set(pages.flatMap(p => p.tags ?? []))].slice(0, 14), [pages])
  const shownPages = useMemo(() => (tag ? pages.filter(p => (p.tags ?? []).includes(tag)) : pages), [pages, tag])
  const pageCards = useMemo(() => (file ? shownPages.map(p => toPageCard(file, p)) : []), [file, shownPages])
  const questionCards = useMemo(
    () => (file ? pages.flatMap(p => (p.regions ?? []).map(r => toQuestionCard(file, p, r))) : []),
    [file, pages],
  )
  const totalQuestions = useMemo(() => pages.reduce((sum, p) => sum + (p.regions?.length ?? p.questions ?? 0), 0), [pages])

  useEffect(() => {
    if (!file || startPage <= 1 || (pages.length === 0 && file.sourceKind !== 'images')) return
    requestAnimationFrame(() => document.getElementById(`page-${startPage}`)?.scrollIntoView({ block: 'start' }))
  }, [file, pages.length, startPage])

  if (q.isError) return <div className="empty"><Mascot size={84} />That file is gone or never existed.</div>

  const jumpToPage = (n: number) => {
    if (tag && !shownPages.some(p => p.number === n)) setTag(null)
    requestAnimationFrame(() => document.getElementById(`page-${n}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
  }

  return (
    <main className="page-main doc-main">
      <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap', justifyContent: 'space-between' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, minWidth: 0 }}>
          <button className="candy c-white rnd" type="button" aria-label="Back" onClick={() => (window.history.length > 1 ? nav(-1) : nav('/browse'))}>
            <IconBack />
          </button>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
            <div className="lbl ellipsis">
              {file ? [file.courseName, TYPE_SINGULAR[file.materialType], file.year, `${pageCount ?? (pages.length || file.pageCount)} pages`].filter(Boolean).join(' · ') : 'Loading…'}
            </div>
            <h1 className="disp ellipsis" style={{ margin: 0, fontSize: 32, lineHeight: 1.05 }} title={file?.title}>{file?.title ?? ' '}</h1>
          </div>
        </div>
        {file && (
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
            {file.courseSlug && <Link className="candy c-white sm" to={`/browse/${file.courseSlug}?file=${file.id}${file.materialType ? `&type=${file.materialType}` : ''}${file.folder ? `&folder=${encodeURIComponent(file.folder)}` : ''}`}>In Browse</Link>}
            {me?.role === 'uploader' && <Link className="candy c-cream sm" to={`/inspect/${file.id}?page=${startPage}`}>Inspect</Link>}
            <a className="candy sm" href={`/api/files/${file.id}/download`}><IconDownload size={16} />Download</a>
          </div>
        )}
      </div>

      {file && pages.length > 0 && (
        <>
          <div className="doc-toolbar">
            <label className="lbl" style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
              Jump to
              <select className="sel" aria-label="Jump to page" defaultValue={String(startPage)} onChange={e => jumpToPage(Number(e.target.value))}
                style={{ minWidth: 120, minHeight: 38, padding: '0 34px 0 14px', fontSize: 14 }}>
                {pages.map(p => <option key={p.id} value={p.number}>Page {p.number}{p.questions > 0 ? ` (${p.questions} Q)` : ''}</option>)}
              </select>
            </label>
            {totalQuestions > 0 && (questionCards.length > 0) && (
              <button className={`chip ${showQuestions ? 'on' : ''}`} type="button" aria-pressed={showQuestions} onClick={() => setShowQuestions(s => !s)}>
                {showQuestions ? `Hide questions (${questionCards.length})` : `Show questions (${questionCards.length})`}
              </button>
            )}
            {tags.length > 0 && (
              <div className="chip-row" style={{ flex: 1, minWidth: 200 }}>
                <span className="lbl" style={{ marginRight: 4 }}>Topics</span>
                {tags.map(t => (
                  <button key={t} className={`chip ${tag === t ? 'on' : ''}`} type="button" aria-pressed={tag === t} onClick={() => setTag(tag === t ? null : t)}>
                    {t}{tag === t && <IconClose size={14} />}
                  </button>
                ))}
              </div>
            )}
            <span className="lbl" style={{ marginLeft: 'auto' }}>
              {tag ? `${shownPages.length} of ${count(pages.length, 'page')}` : count(pages.length, 'page')}
            </span>
          </div>

          <div className="doc-pages">
            {shownPages.map((page, idx) => {
              const card = pageCards[idx]
              const regions = showQuestions ? (page.regions ?? []) : []
              return (
                <div key={page.id} style={{ width: '100%', maxWidth: 1100 }}>
                  <div id={`page-${page.number}`} className="doc-page" style={{ aspectRatio: `${page.width} / ${page.height}` }}>
                    <img src={page.pageUrl ?? `/api/files/${file.id}/view?part=${page.number}`} alt={`Page ${page.number} of ${file.title}`} width={page.width} height={page.height}
                      loading={Math.abs(page.number - startPage) < 3 ? 'eager' : 'lazy'} />
                    {regions.map(r => {
                      const qLabel = r.questionNumber ? `Q${r.questionNumber.replace(/^q/i, '')}` : 'Question'
                      return (
                        <button key={r.id} type="button" className="reg r-question doc-qbox"
                          aria-label={`Open ${qLabel} on page ${page.number}`}
                          title={`Click to view or share ${qLabel}`}
                          style={{ left: `${r.x * 100}%`, top: `${r.y * 100}%`, width: `${r.w * 100}%`, height: `${r.h * 100}%` }}
                          onClick={() => {
                            const qIdx = questionCards.findIndex(c => c.ref === `r:${r.id}`)
                            if (qIdx >= 0) openViewer(questionCards, qIdx)
                          }}>
                          <span>{qLabel}</span>
                        </button>
                      )
                    })}
                    <span className="doc-num">{page.number}</span>
                  </div>
                  <div className="doc-page-actions">
                    {(page.tags ?? []).length > 0 && (
                      <span className="lbl ellipsis" style={{ marginRight: 'auto', textTransform: 'none', letterSpacing: 0 }}>
                        {(page.tags ?? []).join(' · ')}
                      </span>
                    )}
                    <button className="candy c-white sm" type="button" aria-label={`Zoom page ${page.number}`} onClick={() => openViewer(pageCards, idx)}>
                      <IconExpand size={15} />Zoom
                    </button>
                    <button className="candy c-white sm" type="button" aria-label={`Copy link to page ${page.number}`} onClick={() => void copyShareLink(card, toast)}>
                      <IconShare size={15} />Copy link
                    </button>
                    <ReportButton target={{ ref: `p:${page.id}`, fileTitle: file.title, pageNumber: page.number }} />
                  </div>
                </div>
              )
            })}
          </div>
        </>
      )}

      {file && pages.length === 0 && file.sourceKind === 'images' && (
        <div className="doc-pages">
          {Array.from({ length: file.parts }, (_, i) => (
            <div key={i} style={{ width: '100%', maxWidth: 1100 }}>
              <div id={`page-${i + 1}`} className="doc-page">
                <img src={`/api/files/${file.id}/view?part=${i + 1}`} alt={`Photo ${i + 1} of ${file.title}`} loading={Math.abs(i + 1 - startPage) < 3 ? 'eager' : 'lazy'} />
                <span className="doc-num">{i + 1}</span>
              </div>
            </div>
          ))}
        </div>
      )}
      {file && pages.length === 0 && file.sourceKind !== 'images' && (
        <PdfDoc url={`/api/files/${file.id}/view`} startPage={startPage} onPageCount={setPageCount} />
      )}
    </main>
  )
}
