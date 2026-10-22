import { useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import {
  api, count, TYPE_LABELS, TYPE_ORDER, type Course, type CourseListing, type FileDto, type MaterialType, type PageThumb, type ResultCard as Card,
} from '../lib/api'
import { useApp } from '../lib/app'
import { IconBack, IconClose, IconFolder, IconSearch } from '../ui/Icons'
import { Mascot } from '../ui/Mascot'
import { ResultCard } from '../ui/ResultCard'

const KIND_BADGE: Record<FileDto['sourceKind'], { label: (f: FileDto) => string; bg: string; fg?: string }> = {
  pdf: { label: () => 'PDF', bg: '#2F6BFF', fg: '#fff' },
  office: { label: f => (f.originalName.split('.').pop() ?? 'DOC').toUpperCase().slice(0, 4), bg: '#FF8CC2' },
  images: { label: () => 'JPG', bg: '#2BC48A' },
}

export function FileBadge({ file }: { file: FileDto }) {
  const k = KIND_BADGE[file.sourceKind]
  return <span className="ftype" style={{ background: k.bg, color: k.fg }}>{k.label(file)}</span>
}

export function fileMeta(f: FileDto) {
  const pages = f.sourceKind === 'images' ? count(f.pageCount, 'photo') : f.sourceKind === 'office' && f.originalName.match(/\.pptx?$/i) ? count(f.pageCount, 'slide') : count(f.pageCount, 'page')
  return [f.year, f.assessmentLabel, pages].filter(Boolean).join(' · ')
}

export function CourseTree({ courses, active, onPick }: { courses: Course[]; active?: string; onPick?: () => void }) {
  const tree = useMemo(() => {
    const out: { semester: number; groups: { group: string; courses: Course[] }[] }[] = []
    for (const s of [1, 2]) {
      const inSem = courses.filter(c => c.semester === s)
      const groups = [...new Set(inSem.map(c => c.group))].map(g => ({ group: g, courses: inSem.filter(c => c.group === g) }))
      if (inSem.length) out.push({ semester: s, groups })
    }
    return out
  }, [courses])
  return (
    <>
      {tree.map(s => (
        <div key={s.semester} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          <div className="lbl" style={{ padding: '4px 6px' }}>Semester {s.semester}</div>
          {s.groups.map(g => (
            <div key={g.group} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {g.group && <div className="lbl" style={{ padding: '6px 10px 0', fontSize: 11, opacity: 0.8 }}>{g.group}</div>}
              {g.courses.map(c => (
                <Link key={c.slug} to={`/browse/${c.slug}`} className={`course ${active === c.slug ? 'on' : ''}`} onClick={onPick}
                  aria-current={active === c.slug ? 'page' : undefined}>
                  <i style={{ background: c.color }} />
                  <span className="ellipsis" title={c.name}>{c.name}</span>
                  {c.files > 0 && <small>{c.files}</small>}
                </Link>
              ))}
            </div>
          ))}
        </div>
      ))}
    </>
  )
}

function pageCard(file: FileDto, p: PageThumb): Card {
  return {
    ref: `p:${p.id}`, kind: p.kind === 'slide' ? 'slide' : 'page', imageUrl: p.pageUrl, pageImageUrl: p.pageUrl, thumbUrl: p.thumbUrl,
    fileId: file.id, pageId: p.id, regionId: null, fileTitle: file.title, fileName: file.originalName, sourceKind: file.sourceKind,
    courseSlug: file.courseSlug ?? '', courseName: file.courseName ?? '', courseColor: file.courseColor ?? '#FF6B2C', semester: 0,
    materialType: file.materialType, year: file.year, assessmentLabel: file.assessmentLabel, pageNumber: p.number, pageCount: file.pageCount,
    questionNumber: null, tags: p.tags, box: null, width: p.width, height: p.height, saved: false, answerRef: null, answerImageUrl: null,
  }
}

function FilePages({ fileId }: { fileId: string }) {
  const { openViewer, me } = useApp()
  const [tag, setTag] = useState<string | null>(null)
  const q = useQuery({ queryKey: ['file', fileId], queryFn: () => api.get<{ file: FileDto; pages: PageThumb[] }>(`/api/files/${fileId}`) })
  if (q.isLoading) return <div className="pages">{[0, 1, 2, 3].map(i => <div key={i} className="skeleton" style={{ aspectRatio: '3 / 4' }} />)}</div>
  if (!q.data) return null
  const { file, pages } = q.data
  const tags = [...new Set(pages.flatMap(p => p.tags))].slice(0, 12)
  const shown = tag ? pages.filter(p => p.tags.includes(tag)) : pages
  const cards = shown.map(p => pageCard(file, p))
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
      <div className="chip-row">
        <span className="lbl" style={{ marginRight: 4 }}>Tags</span>
        {tags.map(t => (
          <button key={t} className={`chip ${tag === t ? 'on' : ''}`} type="button" aria-pressed={tag === t} onClick={() => setTag(tag === t ? null : t)}>
            {t}{tag === t && <IconClose size={14} />}
          </button>
        ))}
        <span style={{ marginLeft: 'auto', font: "800 14px/1 'Nunito', sans-serif", color: '#7A5A3A' }}>
          {tag ? `${shown.length} of ${count(pages.length, 'page')}` : count(pages.length, 'page')}
        </span>
      </div>
      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        <Link className="candy c-white sm" to={`/files/${file.id}`}>Open original</Link>
        <a className="candy c-cream sm" href={`/api/files/${file.id}/download`}>Download</a>
        {me?.role === 'uploader' && <Link className="candy c-cream sm" to={`/inspect/${file.id}`}>Inspect</Link>}
      </div>
      <div className="pages">
        {cards.map((c, i) => (
          <button key={c.ref} className="pg" type="button" aria-label={`Page ${c.pageNumber}`} onClick={() => openViewer(cards, i)}
            style={{ animation: `pop-in 300ms var(--ease-out) ${Math.min(i, 10) * 50}ms both` }}>
            <span className="pg-img"><img src={c.thumbUrl} alt={`Page ${c.pageNumber}`} loading="lazy" width={c.width} height={c.height} /></span>
            <span className="pg-meta"><span>Page {c.pageNumber}</span><span className="ellipsis">{c.tags.slice(0, 2).join(', ')}</span></span>
          </button>
        ))}
      </div>
    </div>
  )
}

export function Browse() {
  const { slug } = useParams()
  const [params, setParams] = useSearchParams()
  const nav = useNavigate()
  const type = params.get('type') as MaterialType | null
  const folder = params.get('folder') ?? ''
  const fileId = params.get('file')
  const query = params.get('q') ?? ''
  const [draft, setDraft] = useState(query)

  const courses = useQuery({ queryKey: ['courses'], queryFn: () => api.get<Course[]>('/api/courses') })
  const listing = useQuery({
    queryKey: ['course', slug, type, folder],
    queryFn: () => api.get<CourseListing>(`/api/courses/${slug}?${new URLSearchParams({ ...(type ? { type } : {}), ...(folder ? { folder } : {}) })}`),
    enabled: !!slug,
  })
  const search = useQuery({
    queryKey: ['search', slug, type, query],
    queryFn: () => api.get<Card[]>(`/api/search?${new URLSearchParams({ q: query, ...(slug ? { course: slug } : {}), ...(type ? { type } : {}) })}`),
    enabled: query.length > 0,
  })

  const update = (next: Record<string, string | null>) => {
    const p = new URLSearchParams(params)
    for (const [k, v] of Object.entries(next)) { if (v) p.set(k, v); else p.delete(k) }
    setParams(p)
  }
  const submit = (e: FormEvent) => { e.preventDefault(); update({ q: draft.trim() || null }) }

  const course = courses.data?.find(c => c.slug === slug)
  const data = listing.data
  const types = TYPE_ORDER.filter(t => (data?.types[t] ?? 0) > 0)
  const crumbs = folder ? folder.split('/') : []
  const selectedFile = data?.files.find(f => f.id === fileId)

  return (
    <div className="body">
      <aside aria-label="Courses" className={`side browse-side ${slug ? 'hide-sm' : ''}`}>
        <div className="lbl" style={{ padding: '4px 6px' }}>Year 2 courses</div>
        {courses.data ? <CourseTree courses={courses.data} active={slug} /> : [0, 1, 2, 3].map(i => <div key={i} className="skeleton" style={{ height: 52 }} />)}
      </aside>

      <main className={`sheet-main browse-main ${slug ? '' : 'hide-sm'}`}>
        <div className="browse-head">
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0 }}>
            {slug && <button className="candy c-white rnd show-sm" type="button" aria-label="Back to courses" onClick={() => nav('/browse')}><IconBack /></button>}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6, minWidth: 0 }}>
              <nav aria-label="Breadcrumb" className="lbl ellipsis">
                <Link to="/browse" style={{ color: '#7A5A3A' }}>Year 2</Link>
                {course && <> › <Link to={`/browse/${course.slug}`} style={{ color: '#7A5A3A' }}>{course.name}</Link></>}
                {type && <> › <Link to={`/browse/${slug}?type=${type}`} style={{ color: '#7A5A3A' }}>{TYPE_LABELS[type]}</Link></>}
                {crumbs.map((c, i) => (
                  <span key={i}> › <button type="button" className="crumb" onClick={() => update({ folder: crumbs.slice(0, i + 1).join('/'), file: null })}>{c}</button></span>
                ))}
              </nav>
              <h1 className="disp ellipsis" style={{ margin: 0, fontSize: 36, lineHeight: 1.05 }}>
                {selectedFile?.title ?? (crumbs.at(-1) || course?.name || 'Browse the drive')}
              </h1>
            </div>
          </div>
          <form onSubmit={submit} className="search" style={{ flex: '0 1 380px', minWidth: 240 }} role="search">
            <IconSearch size={20} />
            <label htmlFor="q" className="sr">Search by keyword</label>
            <input id="q" type="search" placeholder={course ? `Search ${course.name}` : 'Search every course'} value={draft} onChange={e => setDraft(e.target.value)} />
            {query && <button type="button" className="candy c-white rnd sm" aria-label="Clear search" onClick={() => { setDraft(''); update({ q: null }) }}><IconClose size={14} /></button>}
          </form>
        </div>

        {slug && types.length > 0 && (
          <div role="tablist" aria-label="Material type" className="segs">
            <button className={`seg ${!type ? 'on' : ''}`} type="button" role="tab" aria-selected={!type} onClick={() => update({ type: null, folder: null, file: null })}>All</button>
            {types.map(t => (
              <button key={t} className={`seg ${type === t ? 'on' : ''}`} type="button" role="tab" aria-selected={type === t}
                onClick={() => update({ type: t, folder: null, file: null })}>
                {TYPE_LABELS[t]} <small>{data?.types[t]}</small>
              </button>
            ))}
          </div>
        )}

        {query ? (
          <section aria-label="Search results" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div className="lbl">{search.data ? `${count(search.data.length, 'match', 'matches')} for “${query}”` : 'Searching…'}</div>
            {search.data?.length === 0 && <div className="empty"><Mascot size={72} />Nothing matched. Try other words?</div>}
            <div className="results">{search.data?.map((c, i) => <ResultCard key={c.ref} card={c} index={i} all={search.data!} />)}</div>
          </section>
        ) : !slug ? (
          <div className="empty"><Mascot size={96} mood="happy" />Pick a course on the left, or search everything above.</div>
        ) : (
          <div className="browse-cols">
            <div className={`browse-files ${fileId ? 'hide-sm' : ''}`}>
              {listing.isLoading && [0, 1, 2].map(i => <div key={i} className="skeleton" style={{ height: 64 }} />)}
              {data && data.folders.length === 0 && data.files.length === 0 && (
                <div className="empty"><Mascot size={72} />Nothing here yet. Uploaders can feed the drive from the Upload tab.</div>
              )}
              {data && data.folders.length > 0 && <div className="lbl" style={{ padding: '0 4px' }}>Folders</div>}
              {data?.folders.map(f => (
                <button key={f.path} className="file folder" type="button" onClick={() => update({ folder: f.path, file: null })}>
                  <span className="ftype" style={{ background: '#FFD34D' }}><IconFolder size={20} /></span>
                  <span className="meta"><b>{f.name}</b><span>{count(f.files, 'file')}</span></span>
                </button>
              ))}
              {data && data.files.length > 0 && <div className="lbl" style={{ padding: '6px 4px 0' }}>Files</div>}
              {data?.files.map(f => (
                <button key={f.id} className={`file ${f.id === fileId ? 'on' : ''}`} type="button" aria-pressed={f.id === fileId}
                  onClick={() => update({ file: f.id })}>
                  <FileBadge file={f} />
                  <span className="meta"><b title={f.title}>{f.title}</b><span>{fileMeta(f) || TYPE_LABELS[f.materialType]}</span></span>
                </button>
              ))}
            </div>
            <div className={`browse-pages ${fileId ? '' : 'hide-sm'}`}>
              {fileId ? (
                <>
                  <button className="candy c-white sm show-sm" type="button" style={{ alignSelf: 'flex-start' }} onClick={() => update({ file: null })}>
                    <IconBack size={16} />Files
                  </button>
                  <FilePages key={fileId} fileId={fileId} />
                </>
              ) : (
                <div className="empty"><Mascot size={72} />Pick a file to see its pages.</div>
              )}
            </div>
          </div>
        )}
      </main>
    </div>
  )
}
