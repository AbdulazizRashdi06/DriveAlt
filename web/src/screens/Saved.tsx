import { Link } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, count, type ResultCard, type SavedGroup } from '../lib/api'
import { useApp } from '../lib/app'
import { useSaved } from '../lib/saved'
import { IconBookmark, IconDownload, IconExternal } from '../ui/Icons'
import { Mascot } from '../ui/Mascot'
import { originalHref, resultLabel } from '../ui/Viewer'

function SavedCard({ card, index, all, color }: { card: ResultCard; index: number; all: ResultCard[]; color: string }) {
  const { openViewer, toast } = useApp()
  const qc = useQueryClient()
  const [saved, toggle] = useSaved(card.ref, true)
  const remove = async () => {
    try {
      await toggle()
      toast('Removed from Saved.')
      void qc.invalidateQueries({ queryKey: ['bookmarks'] })
    } catch {
      toast("Couldn't remove that. Try again?")
    }
  }
  if (!saved) return null
  return (
    <article className="saved-card enter" style={{ animationDelay: `${Math.min(index, 8) * 50}ms`, borderColor: color }}>
      <button className="shot" type="button" onClick={() => openViewer(all, index)} aria-label={`Open ${resultLabel(card)} from ${card.fileTitle}`}>
        <img src={card.imageUrl} alt={`Screenshot of ${resultLabel(card)} from ${card.fileTitle}, page ${card.pageNumber}`} loading="lazy" />
      </button>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '2px 4px' }}>
        <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
          <b className="ellipsis" style={{ font: "900 14px/1.2 'Nunito', sans-serif" }} title={card.fileTitle}>{card.fileTitle}</b>
          <span style={{ font: "700 12px/1.2 'Nunito', sans-serif", color: '#7A5A3A' }}>{[resultLabel(card), `p.${card.pageNumber}`, card.year].filter(Boolean).join(' · ')}</span>
        </div>
        <a className="candy c-white rnd sm" href={card.regionId ? `${card.imageUrl.split('?')[0]}?download=true` : `/api/files/${card.fileId}/download`}
          aria-label="Download" title="Download"><IconDownload size={16} /></a>
        <Link className="candy c-white rnd sm" to={originalHref(card)} aria-label="Open original" title="Open original"><IconExternal size={16} /></Link>
        <button className="candy c-pink rnd sm" type="button" aria-label="Remove from Saved" aria-pressed="true" title="Remove" onClick={() => void remove()}>
          <IconBookmark size={16} filled />
        </button>
      </div>
    </article>
  )
}

export function Saved() {
  const q = useQuery({ queryKey: ['bookmarks'], queryFn: () => api.get<SavedGroup[]>('/api/bookmarks') })
  const total = q.data?.reduce((n, g) => n + g.items.length, 0) ?? 0

  return (
    <main className="page-main">
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 16, flexWrap: 'wrap', justifyContent: 'space-between' }}>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 16, flexWrap: 'wrap' }}>
          <h1 className="disp" style={{ margin: 0, fontSize: 44, lineHeight: 1 }}>Saved</h1>
          <span style={{ font: "800 16px/1.4 'Nunito', sans-serif", color: '#7A5A3A' }}>{q.data ? `${count(total, 'screenshot')} in your stash.` : ' '}</span>
        </div>
        {total > 0 && <a className="candy c-white" href="/api/bookmarks/download"><IconDownload />Download all</a>}
      </div>

      {q.isLoading && <div className="saved-row">{[0, 1, 2].map(i => <div key={i} className="skeleton" style={{ height: 220 }} />)}</div>}
      {q.data?.length === 0 && (
        <div className="empty">
          <Mascot size={96} mood="happy" />
          Nothing saved yet. Tap <b>Save</b> on any result and it lands here, grouped by course.
          <Link className="candy" to="/chat">Go find something</Link>
        </div>
      )}

      {q.data?.map(g => (
        <section key={g.course} aria-labelledby={`g-${g.course}`} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <i style={{ width: 16, height: 16, borderRadius: 999, background: g.color, flex: 'none' }} />
            <h2 id={`g-${g.course}`} className="disp" style={{ margin: 0, fontSize: 26 }}>{g.name}</h2>
            <span className="lbl">{count(g.items.length, 'item')}</span>
            <a className="candy c-cream sm" href={`/api/bookmarks/download?course=${g.course}`} style={{ marginLeft: 'auto' }}><IconDownload size={16} />ZIP</a>
          </div>
          <div className="saved-row">
            {g.items.map((c, i) => <SavedCard key={c.ref} card={c} index={i} all={g.items} color={g.color} />)}
          </div>
        </section>
      ))}
    </main>
  )
}
