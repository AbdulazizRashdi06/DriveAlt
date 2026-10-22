import { Link } from 'react-router'
import type { ResultCard as Card } from '../lib/api'
import { CARD_COLORS, onDark } from '../lib/api'
import { useApp } from '../lib/app'
import { useSaved } from '../lib/saved'
import { IconExpand, IconExternal } from './Icons'
import { originalHref, resultLabel } from './Viewer'

const TILTS = [-1.5, 1, -0.5, 0.75]

/** A result: always a screenshot of the original, never text. */
export function ResultCard({ card, index, all }: { card: Card; index: number; all: Card[] }) {
  const { openViewer, toast } = useApp()
  const [saved, toggle] = useSaved(card.ref, card.saved)
  const color = CARD_COLORS[index % CARD_COLORS.length]
  const dark = onDark(color)
  const meta = [card.fileTitle, `p.${card.pageNumber}`, card.year].filter(Boolean).join(' · ')
  const top = card.sourceKind === 'images' && card.pageCount === 1 ? `PHOTO · ${card.courseName}` : card.courseName

  const save = async () => {
    try {
      const now = await toggle()
      toast(now ? 'Saved! Find it in the Saved tab.' : 'Removed from Saved.')
    } catch {
      toast("Couldn't save that. Try again?")
    }
  }

  return (
    <article className={`card enter ${dark ? 'on-dark' : ''}`}
      style={{ background: color, transform: `rotate(${TILTS[index % TILTS.length]}deg)`, animationDelay: `${Math.min(index, 8) * 55}ms` }}>
      <div className="card-top" style={{ color: dark ? '#fff' : undefined }}>
        <span className="ellipsis" title={card.courseName}>{top}</span>
        <span>{resultLabel(card)}</span>
      </div>
      <button className="shot" type="button" aria-label={`Open ${resultLabel(card)} from ${card.fileTitle}, page ${card.pageNumber}`}
        onClick={() => openViewer(all, index)}>
        <img src={card.imageUrl} alt={`Screenshot of ${resultLabel(card)} from ${card.fileTitle}, page ${card.pageNumber}`} loading="lazy" />
      </button>
      <div className="card-meta ellipsis" style={{ color: dark ? '#fff' : undefined }} title={meta}>{meta}</div>
      {card.tags.length > 0 && (
        <div className="tags">{card.tags.slice(0, 3).map(t => <span key={t} className="tg">{t}</span>)}</div>
      )}
      <div className="card-actions">
        <button className="candy c-white rnd" type="button" aria-label="Open full page" title="Open full page" onClick={() => openViewer(all, index)}>
          <IconExpand />
        </button>
        <Link className="candy c-white rnd" to={originalHref(card)} aria-label={`Open original file at page ${card.pageNumber}`} title="Open original file">
          <IconExternal />
        </Link>
        <button className={`candy ${saved ? 'c-pink saved-pop' : 'c-white'}`} key={String(saved)} type="button" aria-pressed={saved}
          style={{ minHeight: 44, flex: 1 }} onClick={() => void save()}>
          {saved ? 'Saved!' : 'Save'}
        </button>
      </div>
    </article>
  )
}
