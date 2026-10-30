import { Link } from 'react-router'
import type { ResultCard as Card } from '../lib/api'
import { CARD_COLORS, onDark, TYPE_SINGULAR } from '../lib/api'
import { useApp } from '../lib/app'
import { useSaved } from '../lib/saved'
import { IconDownload, IconExpand, IconExternal, IconShare } from './Icons'
import { copyShareLink, originalHref, resultLabel } from './Viewer'

const TILTS = [-1.5, 1, -0.5, 0.75]

type Props = { card: Card; index: number; all: Card[] }

/** File requests open/download the whole original; question requests keep the page/crop viewer. */
export function ResultCard(props: Props) {
  return props.card.kind === 'file' ? <FileResultCard card={props.card} index={props.index} /> : <QuestionResultCard {...props} />
}

function FileResultCard({ card, index }: Pick<Props, 'card' | 'index'>) {
  const color = CARD_COLORS[index % CARD_COLORS.length]
  const dark = onDark(color)
  return <article className={`card file-card enter ${dark ? 'on-dark' : ''}`} style={{ background: color, transform: `rotate(${TILTS[index % TILTS.length]}deg)`, animationDelay: `${Math.min(index, 8) * 55}ms` }}>
    <div className="card-top" style={{ color: dark ? '#fff' : undefined }}>
      <span className="ellipsis" title={card.courseName}>{card.courseName}</span><span>Full file</span>
    </div>
    <div className="shot-wrap">
      <Link className="shot" to={originalHref(card)} aria-label={`Open full file ${card.fileTitle}`}>
        <img className="shot-img" src={card.thumbUrl} loading="lazy" alt={`Page ${card.pageNumber} preview of ${card.fileTitle}`} />
      </Link>
    </div>
    <div className="card-meta" style={{ color: dark ? '#fff' : undefined, overflowWrap: 'anywhere' }}>{card.fileTitle}</div>
    <div className="tags"><span className="tg">{TYPE_SINGULAR[card.materialType]}</span><span className="tg">{card.pageCount} {card.pageCount === 1 ? 'page' : 'pages'}</span>{card.year && <span className="tg">{card.year}</span>}</div>
    <div className="card-actions">
      <Link className="candy c-white" style={{ flex: 1 }} to={originalHref(card)}><IconExternal />Open file</Link>
      <a className="candy c-white" style={{ flex: 1 }} href={`/api/files/${card.fileId}/download`} aria-label={`Download full file ${card.fileTitle}`}><IconDownload />Download</a>
    </div>
  </article>
}

function QuestionResultCard({ card, index, all }: Props) {
  const { openViewer, toast } = useApp()
  const [saved, toggle] = useSaved(card.ref, card.saved)
  const color = CARD_COLORS[index % CARD_COLORS.length]
  const dark = onDark(color)
  const meta = [card.fileTitle, `p.${card.pageNumber}`, card.year].filter(Boolean).join(' · ')
  const open = () => {
    const questions = all.filter(r => r.kind !== 'file')
    openViewer(questions, Math.max(0, questions.findIndex(r => r.ref === card.ref)))
  }
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
      <div className="shot-wrap">
        <button className="shot" type="button" aria-label={`Open ${resultLabel(card)} from ${card.fileTitle}, page ${card.pageNumber}`}
          onClick={open}>
          <img className="shot-img" src={card.kind === 'question' ? card.imageUrl : card.thumbUrl} loading="lazy"
            alt={`Screenshot of ${resultLabel(card)} from ${card.fileTitle}, page ${card.pageNumber}`} />
        </button>
      </div>
      <div className="card-meta ellipsis" style={{ color: dark ? '#fff' : undefined }} title={meta}>{meta}</div>
      {card.tags.length > 0 && (
        <div className="tags">{card.tags.slice(0, 3).map(t => <span key={t} className="tg">{t}</span>)}</div>
      )}
      <div className="card-actions">
        <button className="candy c-white rnd" type="button" aria-label="Open full page" title="Open full page" onClick={open}>
          <IconExpand />
        </button>
        <Link className="candy c-white rnd" to={originalHref(card)} aria-label={`Open original file at page ${card.pageNumber}`} title="Open original file">
          <IconExternal />
        </Link>
        <button className="candy c-white rnd" type="button" aria-label={`Copy link to ${resultLabel(card)}`} title="Copy shareable link" onClick={() => void copyShareLink(card, toast)}>
          <IconShare />
        </button>
        <button className={`candy ${saved ? 'c-pink saved-pop' : 'c-white'}`} key={String(saved)} type="button" aria-pressed={saved}
          style={{ minHeight: 44, flex: 1 }} onClick={() => void save()}>
          {saved ? 'Saved!' : 'Save'}
        </button>
      </div>
    </article>
  )
}
