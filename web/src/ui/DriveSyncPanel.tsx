import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../lib/api'
import { useApp } from '../lib/app'
import { IconFolder } from './Icons'

interface FolderStatus { state: 'missing' | 'working' | 'done' | 'unknown'; files: number; missing: number; working: number; failed: number }
interface Folder { id: string; parentId?: string | null; name: string; segments: string[]; enabled: boolean; recursive?: boolean; available: boolean; checkedAt: string | null; error: string | null; status?: FolderStatus | null }
interface DriveFile { id: string; name: string; importable: boolean; fileId: string | null; fileStatus?: string | null }
type Mode = 'off' | 'only' | 'all'
type SetMode = (f: Folder, mode: Mode) => void
interface SyncState { enabled: boolean; scanRequested: boolean; startedAt: string | null; finishedAt: string | null; error: string | null; imported: number; folders: Folder[] }

export function DriveSyncPanel() {
  const { toast } = useApp()
  const qc = useQueryClient()
  const [filter, setFilter] = useState('')
  const [path, setPath] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const sync = useQuery({ queryKey: ['drive-sync'], queryFn: () => api.get<SyncState>('/api/admin/drive-sync/'), refetchInterval: 5000 })
  const change = async (path: string, enabled?: boolean) => {
    setBusy(true)
    try {
      if (enabled === undefined) await api.post(path, {})
      else await api.put(path, { enabled })
      await qc.invalidateQueries({ queryKey: ['drive-sync'] })
    } catch (e) { toast((e as Error).message) }
    finally { setBusy(false) }
  }
  const setMode: SetMode = (f, mode) => {
    setBusy(true)
    api.put(`/api/admin/drive-sync/folders/${encodeURIComponent(f.id)}`, { mode })
      .then(() => qc.invalidateQueries({ queryKey: ['drive-sync'] }))
      .catch(e => toast((e as Error).message))
      .finally(() => setBusy(false))
  }
  const s = sync.data
  const scanning = !!s && (s.scanRequested || (!!s.startedAt && !s.finishedAt && Date.now() - new Date(s.startedAt).getTime() < 15 * 60 * 1000))
  const known = s?.folders.filter(f => f.available) ?? []
  const checked = s?.startedAt ? known.filter(f => f.checkedAt && new Date(f.checkedAt) >= new Date(s.startedAt!)).length : 0
  const queued = !!s?.scanRequested && (!s.startedAt || !!s.finishedAt)
  const progress = `${checked} of ${known.length} known folders checked · ${s?.imported ?? 0} file groups imported`
  return <section className="drive-sync" aria-labelledby="drive-sync-heading">
    <div className="drive-sync-head">
      <div><h2 className="disp" id="drive-sync-heading">Google Drive sync</h2>
        <p>Choose which Year 2 folders to import. New files in selected folders are checked every five minutes.</p></div>
      <label className="drive-sync-toggle"><input type="checkbox" role="switch" aria-label="Automatic Drive sync" checked={s?.enabled ?? false}
        disabled={busy || !s} onChange={e => void change('/api/admin/drive-sync/', e.target.checked)} />{s?.enabled ? 'Auto-sync on' : 'Auto-sync paused'}</label>
    </div>
    <p>Pick folders, then turn on auto-sync. “This folder” imports only its own files; “+ Subfolders” also imports everything below it, including folders added later. Red folders have files that aren’t in the app yet, yellow ones are still processing or failed, green ones are all in. Pausing stops new imports; current processing finishes.</p>
    <div className="drive-sync-head">
      <button type="button" className="candy c-blue sm" disabled={busy || scanning || !s} onClick={() => void change('/api/admin/drive-sync/scan')}>
        {scanning ? 'Checking Drive…' : s?.enabled ? 'Sync now' : 'Discover folders'}
      </button>
      <span role="status">{scanning
        ? queued ? 'Scan queued…' : progress
        : s?.finishedAt ? `Last checked ${new Date(s.finishedAt).toLocaleString()} · ${s.imported} file groups imported` : 'No completed scan yet'}</span>
    </div>
    {s && (scanning || s.startedAt) && <progress className="drive-sync-progress" aria-label="Drive folder scan progress"
      aria-valuetext={queued ? 'Scan queued' : progress} max={known.length || 1}
      value={queued || known.length === 0 ? undefined : checked} />}
    {sync.isError && <p role="alert">Couldn’t load Drive sync settings. Please try again.</p>}
    {s?.error && <p role="alert">{s.error}</p>}
    {s && s.folders.length === 0 && <p>Discover folders first, select the ones you want, then turn on automatic sync. Discovery while paused only lists folders.</p>}
    {!!s?.folders.length && <>
      <label className="drive-sync-search">Find a folder<input className="input" value={filter} onChange={e => setFilter(e.target.value)} placeholder="Semester, course or folder name" /></label>
      {filter
        ? <div className="drive-sync-folders">
            {s.folders.filter(f => [f.name, ...f.segments].join('/').toLowerCase().includes(filter.toLowerCase())).map(f =>
              <FolderRow key={f.id} folder={f} folders={s.folders} busy={busy} onMode={setMode} onOpen={() => { setPath(f.segments); setFilter('') }} showPath />)}
          </div>
        : <FolderView folders={s.folders} path={path} busy={busy} onMode={setMode} onNavigate={setPath} />}
    </>}
    <p>Files already in the app stay available. Editing or deleting a Drive file does not change its imported copy.</p>
  </section>
}

const same = (a: string[], b: string[]) => a.length === b.length && a.every((x, i) => x.toLowerCase() === b[i].toLowerCase())

const modeOf = (f: Folder): Mode => !f.enabled ? 'off' : f.recursive ? 'all' : 'only'
/** The folder above (by parent link) that syncs everything below it, if any. */
function coveredBy(folders: Folder[], f: Folder) {
  for (let p = folders.find(o => o.id === f.parentId), depth = 0; p && depth < 25; p = folders.find(o => o.id === p!.parentId), depth++)
    if (p.enabled && p.recursive) return p
}

function ModePicker({ folder: f, folders, busy, onMode }: { folder: Folder; folders: Folder[]; busy: boolean; onMode: SetMode }) {
  const mode = modeOf(f)
  const parent = mode === 'off' ? coveredBy(folders, f) : undefined
  return <div className="drive-sync-mode">
    <div className="segs" role="group" aria-label={`Sync ${f.segments.join(' / ') || 'Year 2'}`}>
      {([['off', 'Off'], ['only', 'This folder'], ['all', '+ Subfolders']] as const).map(([m, text]) =>
        <button key={m} type="button" className={`seg ${mode === m ? 'on' : ''}`} aria-pressed={mode === m} disabled={busy || !f.available}
          onClick={() => mode !== m && onMode(f, m)}>{text}</button>)}
    </div>
    {parent && <small>On through {parent.name || 'Year 2'}</small>}
  </div>
}

function StatusBadge({ status }: { status?: FolderStatus | null }) {
  const s = status ?? { state: 'unknown', files: 0, missing: 0, working: 0, failed: 0 }
  const text = s.state === 'missing' ? `${s.missing} not uploaded`
    : s.state === 'working' ? [s.working && `${s.working} processing`, s.failed && `${s.failed} failed`].filter(Boolean).join(', ')
    : s.state === 'done' ? `All ${s.files} uploaded` : 'Not scanned yet'
  return <span className={`drive-sync-status ${s.state}`}><i aria-hidden="true" />{text}</span>
}

function FolderRow({ folder: f, folders, busy, onMode, onOpen, showPath = false }: { folder: Folder; folders: Folder[]; busy: boolean; onMode: SetMode; onOpen: () => void; showPath?: boolean }) {
  return <div className={`drive-sync-folder status-${f.status?.state ?? 'unknown'}`}>
    <button type="button" className="drive-sync-open" onClick={onOpen}>
      <b><IconFolder size={18} /> {f.name}</b>
      <StatusBadge status={f.status} />
      {(showPath || !f.available) && <small>{showPath ? f.segments.slice(0, -1).join(' / ') || 'Year 2' : ''}{!f.available ? ' · no longer listed' : ''}</small>}
    </button>
    <ModePicker folder={f} folders={folders} busy={busy} onMode={onMode} />
    {f.error && <p role="status">{f.error}</p>}
  </div>
}

/** One Drive folder at a time: breadcrumbs, its own switch and files, then its subfolders. */
function FolderView({ folders, path, busy, onMode, onNavigate }: { folders: Folder[]; path: string[]; busy: boolean;
  onMode: SetMode; onNavigate: (path: string[]) => void }) {
  const current = folders.find(f => same(f.segments, path))
  const children = folders.filter(f => f.segments.length === path.length + 1 && same(f.segments.slice(0, path.length), path))
    .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }))
  const files = useQuery({
    queryKey: ['drive-sync-files', current?.id], enabled: !!current,
    queryFn: () => api.get<DriveFile[]>(`/api/admin/drive-sync/folders/${encodeURIComponent(current!.id)}/files`),
  })
  const uploaded = files.data?.filter(f => f.fileId).length ?? 0
  // Kept while moving between folders, so you can walk the tree looking only at what's missing.
  const [missingOnly, setMissingOnly] = useState(false)
  const shown = files.data?.filter(f => !missingOnly || (!f.fileId && f.importable)) ?? []
  return <div className="drive-sync-tree">
    <nav aria-label="Drive folder path" className="drive-sync-crumbs">
      <button type="button" className="crumb" onClick={() => onNavigate([])}>Year 2</button>
      {path.map((p, i) => <span key={i}> › <button type="button" className="crumb" onClick={() => onNavigate(path.slice(0, i + 1))}>{p}</button></span>)}
    </nav>
    {current && <div className={`drive-sync-folder drive-sync-current status-${current.status?.state ?? 'unknown'}`}>
      <span><b>Sync {path.at(-1) ?? 'Year 2'}</b><StatusBadge status={current.status} />
        <small>This folder: only its own files. + Subfolders: everything below it too, including folders added later.</small></span>
      <ModePicker folder={current} folders={folders} busy={busy} onMode={onMode} />
      <a href={`https://drive.google.com/drive/folders/${encodeURIComponent(current.id)}`} target="_blank" rel="noreferrer">Open Drive</a>
      {current.error && <p role="status">{current.error}</p>}
    </div>}
    {children.length > 0 && <div className="drive-sync-folders" aria-label="Subfolders" role="group">
      {children.map(f => <FolderRow key={f.id} folder={f} folders={folders} busy={busy} onMode={onMode} onOpen={() => onNavigate(f.segments)} />)}
    </div>}
    {current && <div className="drive-sync-files" role="group" aria-label="Files in this folder">
      {files.isPending && <p>Listing files on Drive…</p>}
      {files.isError && <p role="alert">Couldn’t list this folder on Drive. Try again in a moment.</p>}
      {files.data && (files.data.length === 0
        ? <p>No files directly in this folder.</p>
        : <>
            <div className="drive-sync-filter">
              <p className="drive-sync-count">{uploaded} of {files.data.length} uploaded</p>
              <div className="segs" aria-label="Show files">
                {([false, true] as const).map(m => <button key={String(m)} type="button" className={`seg ${missingOnly === m ? 'on' : ''}`}
                  aria-pressed={missingOnly === m} onClick={() => setMissingOnly(m)}>{m ? 'Not uploaded' : 'All'}</button>)}
              </div>
            </div>
            {missingOnly && shown.length === 0 && <p>Everything in this folder is uploaded.</p>}
            <ul>
              {shown.map(f => <li key={f.id} className="drive-sync-file">
                <span className="ellipsis" title={f.name}>{f.name}</span>
                {f.fileId
                  ? f.fileStatus && f.fileStatus !== 'ready'
                    ? <Link className="tg drive-sync-working" to={`/files/${f.fileId}`}>{f.fileStatus === 'failed' ? 'Failed' : 'Processing'}</Link>
                    : <Link className="tg drive-sync-up" to={`/files/${f.fileId}`}>Uploaded</Link>
                  : <span className={`tg ${f.importable ? 'drive-sync-down' : 'drive-sync-skip'}`}>{f.importable ? 'Not uploaded' : 'Skipped type'}</span>}
              </li>)}
            </ul>
          </>)}
    </div>}
    {!current && path.length > 0 && <p>This folder isn’t known yet. Run “Discover folders”.</p>}
  </div>
}
