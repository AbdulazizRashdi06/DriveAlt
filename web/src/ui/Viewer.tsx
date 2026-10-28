import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { TransformComponent, TransformWrapper, type ReactZoomPanPinchContentRef } from 'react-zoom-pan-pinch'
import type { ResultCard } from '../lib/api'
import { CARD_COLORS } from '../lib/api'
import { useSaved } from '../lib/saved'
import { IconBookmark, IconClose, IconExternal, IconLeft, IconMinus, IconPlus, IconRight } from './Icons'
import { ReportButton } from './ReportButton'

export function originalHref(r: Pick<ResultCard, 'fileId' | 'pageNumber'>) {
  return `/files/${r.fileId}?page=${r.pageNumber}`
}

export function resultLabel(r: ResultCard) {
  if (r.kind === 'file') return 'Full file'
  const q = r.questionNumber ? `Q${r.questionNumber.replace(/^q/i, '')}` : null
  if (r.kind === 'question') return q ?? 'Question'
  if (r.kind === 'answer') return q ? `Answer · ${q}` : 'Answer'
  return r.kind === 'slide' ? `Slide ${r.pageNumber}` : `Page ${r.pageNumber}`
}

/** Full-page viewer: the matched region outlined on the page, zoom, flip through results. */
export function Viewer({ items, start, onClose }: { items: ResultCard[]; start: number; onClose: () => void }) {
  const [index, setIndex] = useState(Math.min(Math.max(start, 0), items.length - 1))
  const [scale, setScale] = useState(1)
  const zoomRef = useRef<ReactZoomPanPinchContentRef | null>(null)
  const closeRef = useRef<HTMLButtonElement>(null)
  const item = items[index]
  const [saved, toggleSaved] = useSaved(item.ref, item.saved)

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    closeRef.current?.focus()
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = prevOverflow
      previous?.focus?.()
    }
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
      else if (e.key === 'ArrowRight') setIndex(i => Math.min(i + 1, items.length - 1))
      else if (e.key === 'ArrowLeft') setIndex(i => Math.max(i - 1, 0))
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [items.length, onClose])

  useEffect(() => {
    zoomRef.current?.resetTransform(0)
    setScale(1)
  }, [index])

  const go = (d: number) => setIndex(i => Math.min(Math.max(i + d, 0), items.length - 1))
  const meta = [item.courseName, item.year, `page ${item.pageNumber} of ${item.pageCount}`].filter(Boolean).join(' · ')
  const box = item.box

  return (
    <div className="viewer" role="dialog" aria-modal="true" aria-label={`Viewing ${item.fileTitle} page ${item.pageNumber}`}>
      <header className="viewer-head">
        <div style={{ display: 'flex', alignItems: 'center', gap: 16, minWidth: 0, flex: '1 1 auto' }}>
          <button ref={closeRef} className="candy c-white rnd" type="button" aria-label="Close viewer" title="Close (Esc)" onClick={onClose}>
            <IconClose size={20} />
          </button>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4, minWidth: 0 }}>
            <div className="disp ellipsis" style={{ fontSize: 24, lineHeight: 1 }} title={item.fileTitle}>{item.fileTitle}</div>
            <div className="lbl ellipsis">{meta}</div>
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <div className="zoom hide-phone" role="group" aria-label="Zoom">
            <button className="candy c-dark rnd sm" type="button" aria-label="Zoom out" onClick={() => zoomRef.current?.zoomOut(0.4)}><IconMinus /></button>
            <span style={{ minWidth: 56, textAlign: 'center', font: "900 14px/1 'Nunito', sans-serif" }}>{Math.round(scale * 100)}%</span>
            <button className="candy c-dark rnd sm" type="button" aria-label="Zoom in" onClick={() => zoomRef.current?.zoomIn(0.4)}><IconPlus /></button>
          </div>
          <Link className="candy c-white hide-phone" to={originalHref(item)} onClick={onClose}><IconExternal />Open original</Link>
          <ReportButton key={item.ref} target={item} compact />
          <button className={`candy ${saved ? 'c-pink' : 'c-white'} ${saved ? 'saved-pop' : ''}`} key={String(saved)} type="button"
            aria-pressed={saved} aria-label={saved ? 'Saved' : 'Save'} onClick={() => void toggleSaved()}>
            <IconBookmark filled={saved} /><span className="hide-phone">{saved ? 'Saved' : 'Save'}</span>
          </button>
        </div>
      </header>

      <div className="viewer-stage">
        <button className="candy c-white big rnd" type="button" aria-label="Previous result" title="Previous (←)" disabled={index === 0} onClick={() => go(-1)}>
          <IconLeft size={26} />
        </button>
        <TransformWrapper ref={zoomRef} minScale={1} maxScale={6} doubleClick={{ mode: 'toggle', step: 1.5 }}
          onTransform={(_, s) => setScale(s.scale)}>
          <TransformComponent wrapperStyle={{ height: '100%', maxWidth: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', overflow: 'visible' }}>
            <figure className="viewer-fig" style={{ aspectRatio: `${item.width} / ${item.height}` }} key={index === start ? 'first' : 'flip'}>
              <img src={item.pageImageUrl} alt={`${item.fileTitle}, page ${item.pageNumber}${box ? ', with your result outlined' : ''}`}
                width={item.width} height={item.height} draggable={false} />
              {box && (
                <>
                  <div className="hl" aria-hidden="true" style={{ left: `${box.x * 100}%`, top: `${box.y * 100}%`, width: `${box.w * 100}%`, height: `${box.h * 100}%` }} />
                  <span className="hl-tag" style={{ left: `${box.x * 100}%`, top: `calc(${box.y * 100}% - ${box.y < 0.04 ? -8 : 34}px)` }}>
                    Your result · {resultLabel(item)}
                  </span>
                </>
              )}
            </figure>
          </TransformComponent>
        </TransformWrapper>
        <button className="candy c-white big rnd" type="button" aria-label="Next result" title="Next (→)" disabled={index === items.length - 1} onClick={() => go(1)}>
          <IconRight size={26} />
        </button>
      </div>

      {items.length > 1 && (
        <div className="dots" aria-label={`Result ${index + 1} of ${items.length}`} style={{ display: 'none' }} data-phone-dots>
          {items.map((r, i) => <i key={r.ref} className={i === index ? 'on' : ''} />)}
        </div>
      )}

      <footer className="thumbs">
        <span className="lbl">Result {index + 1} of {items.length}</span>
        {items.length > 1 && items.slice(Math.max(0, index - 4), index + 5).map(r => {
          const i = items.indexOf(r)
          return (
            <button key={r.ref} className={`thumb ${i === index ? 'on' : ''}`} type="button" aria-label={`Result ${i + 1}: ${r.fileTitle} ${resultLabel(r)}`}
              aria-current={i === index} style={{ background: CARD_COLORS[i % CARD_COLORS.length] }} onClick={() => setIndex(i)}>
              <img src={r.kind === 'question' ? r.imageUrl : r.thumbUrl} alt="" />
            </button>
          )
        })}
        <span className="lbl" style={{ color: '#C9B48C', textTransform: 'none', letterSpacing: 0 }}>← → to flip · Esc to close</span>
      </footer>

      <footer className="viewer-foot">
        <button className="candy c-white rnd" type="button" aria-label="Previous result" disabled={index === 0} onClick={() => go(-1)}><IconLeft size={22} /></button>
        <Link className="candy" to={originalHref(item)} style={{ flex: 1 }} onClick={onClose}>Open original at p.{item.pageNumber}</Link>
        <button className="candy c-white rnd" type="button" aria-label="Next result" disabled={index === items.length - 1} onClick={() => go(1)}><IconRight size={22} /></button>
      </footer>
    </div>
  )
}
