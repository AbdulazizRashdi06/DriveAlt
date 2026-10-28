import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import * as pdfjs from 'pdfjs-dist'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'
import { api, TYPE_SINGULAR, type FileDto, type PageThumb } from '../lib/api'
import { useApp } from '../lib/app'
import { IconBack, IconDownload } from '../ui/Icons'
import { Mascot } from '../ui/Mascot'
import { ReportButton, type ReportTarget } from '../ui/ReportButton'

pdfjs.GlobalWorkerOptions.workerSrc = workerUrl

/** Renders one PDF page when it scrolls near the viewport. */
function PdfPage({ doc, number, width, target }: { doc: PDFDocumentProxy; number: number; width: number; target?: ReportTarget }) {
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
      {target && <div className="doc-page-actions"><ReportButton target={target} /></div>}
    </div>
  )
}

function PdfDoc({ url, startPage, onPageCount, pages, title }: { url: string; startPage: number; onPageCount: (n: number) => void; pages: PageThumb[]; title: string }) {
  const [doc, setDoc] = useState<PDFDocumentProxy | null>(null)
  const [error, setError] = useState<string | null>(null)
  const wrap = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)
  const scrolled = useRef(false)

  useEffect(() => {
    const task = pdfjs.getDocument({ url, withCredentials: true })
    task.promise.then(d => { setDoc(d); onPageCount(d.numPages) }, e => setError(String(e?.message ?? e)))
    return () => { void task.destroy() }
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
      {doc && width > 0 && Array.from({ length: doc.numPages }, (_, i) => {
        const page = pages.find(p => p.number === i + 1)
        return <PdfPage key={i} doc={doc} number={i + 1} width={width} target={page ? { ref: `p:${page.id}`, fileTitle: title, pageNumber: page.number } : undefined} />
      })}
    </div>
  )
}

/** "Open original": the untouched file (it may contain answers — the bot just never repeats them). */
export function FileView() {
  const { id } = useParams()
  const [params] = useSearchParams()
  const nav = useNavigate()
  const { me } = useApp()
  const startPage = Math.max(1, Number(params.get('page') ?? 1) || 1)
  const [pageCount, setPageCount] = useState<number | null>(null)
  const q = useQuery({ queryKey: ['file', id], queryFn: () => api.get<{ file: FileDto; pages: PageThumb[] }>(`/api/files/${id}`) })
  const file = q.data?.file

  useEffect(() => {
    if (file?.sourceKind !== 'images') return
    requestAnimationFrame(() => document.getElementById(`page-${startPage}`)?.scrollIntoView({ block: 'start' }))
  }, [file?.sourceKind, startPage])

  if (q.isError) return <div className="empty"><Mascot size={84} />That file is gone or never existed.</div>

  return (
    <main className="page-main doc-main">
      <div style={{ display: 'flex', alignItems: 'center', gap: 14, flexWrap: 'wrap', justifyContent: 'space-between' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14, minWidth: 0 }}>
          <button className="candy c-white rnd" type="button" aria-label="Back" onClick={() => (window.history.length > 1 ? nav(-1) : nav('/browse'))}>
            <IconBack />
          </button>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
            <div className="lbl ellipsis">
              {file ? [file.courseName, TYPE_SINGULAR[file.materialType], file.year, `${pageCount ?? file.pageCount} pages`].filter(Boolean).join(' · ') : 'Loading…'}
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

      {file && file.sourceKind === 'images' && (
        <div className="doc-pages">
          {Array.from({ length: file.parts }, (_, i) => {
            const page = q.data?.pages.find(p => p.number === i + 1)
            return <div key={i}>
              <div id={`page-${i + 1}`} className="doc-page">
                <img src={`/api/files/${file.id}/view?part=${i + 1}`} alt={`Photo ${i + 1} of ${file.title}`} loading={Math.abs(i + 1 - startPage) < 3 ? 'eager' : 'lazy'} />
                <span className="doc-num">{i + 1}</span>
              </div>
              {page && <div className="doc-page-actions"><ReportButton target={{ ref: `p:${page.id}`, fileTitle: file.title, pageNumber: page.number }} /></div>}
            </div>
          })}
        </div>
      )}
      {file && file.sourceKind !== 'images' && <PdfDoc url={`/api/files/${file.id}/view`} startPage={startPage} onPageCount={setPageCount} pages={q.data?.pages ?? []} title={file.title} />}
    </main>
  )
}
