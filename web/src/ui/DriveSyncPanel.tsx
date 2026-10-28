import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../lib/api'
import { useApp } from '../lib/app'

interface Folder { id: string; name: string; segments: string[]; enabled: boolean; available: boolean; checkedAt: string | null; error: string | null }
interface SyncState { enabled: boolean; scanRequested: boolean; startedAt: string | null; finishedAt: string | null; error: string | null; imported: number; folders: Folder[] }

export function DriveSyncPanel() {
  const { toast } = useApp()
  const qc = useQueryClient()
  const [filter, setFilter] = useState('')
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
  const s = sync.data
  const scanning = !!s && (s.scanRequested || (!!s.startedAt && !s.finishedAt && Date.now() - new Date(s.startedAt).getTime() < 15 * 60 * 1000))
  return <section className="drive-sync" aria-labelledby="drive-sync-heading">
    <div className="drive-sync-head">
      <div><h2 className="disp" id="drive-sync-heading">Google Drive sync</h2>
        <p>Choose which Year 2 folders to import. New files in selected folders are checked every five minutes.</p></div>
      <label className="drive-sync-toggle"><input type="checkbox" role="switch" aria-label="Automatic Drive sync" checked={s?.enabled ?? false}
        disabled={busy || !s} onChange={e => void change('/api/admin/drive-sync/', e.target.checked)} />{s?.enabled ? 'Auto-sync on' : 'Auto-sync paused'}</label>
    </div>
    <p>Starts paused. Every folder starts off, including newly discovered folders. A switch applies only to files directly in that folder; subfolders have their own switches. Turning it off stops future imports; work already queued continues.</p>
    <div className="drive-sync-head">
      <button type="button" className="btn secondary" disabled={busy || scanning || !s} onClick={() => void change('/api/admin/drive-sync/scan')}>
        {scanning ? 'Checking Drive…' : s?.enabled ? 'Sync now' : 'Discover folders'}
      </button>
      <span>{s?.finishedAt ? `Last checked ${new Date(s.finishedAt).toLocaleString()} · ${s.imported} file groups imported` : 'No completed scan yet'}</span>
    </div>
    {sync.isError && <p role="alert">Couldn’t load Drive sync settings. Please try again.</p>}
    {s?.error && <p role="alert">{s.error}</p>}
    {s && s.folders.length === 0 && <p>Discover folders first, select the ones you want, then turn on automatic sync. Discovery while paused only lists folders.</p>}
    {!!s?.folders.length && <>
      <label className="drive-sync-search">Find a folder<input value={filter} onChange={e => setFilter(e.target.value)} placeholder="Semester, course or folder name" /></label>
      <div className="drive-sync-folders">
        {s.folders.filter(f => [f.name, ...f.segments].join('/').toLowerCase().includes(filter.toLowerCase())).map(f => <div key={f.id} className="drive-sync-folder">
          <label><input type="checkbox" role="switch" aria-label={`Sync ${f.segments.join(' / ') || f.name}`} checked={f.enabled} disabled={busy || !f.available}
            onChange={e => void change(`/api/admin/drive-sync/folders/${encodeURIComponent(f.id)}`, e.target.checked)} />
            <span><b>{f.name}</b><small>{f.segments.join(' / ') || 'Year 2 root'}{!f.available ? ' · no longer listed' : ''}</small></span></label>
          <a href={`https://drive.google.com/drive/folders/${encodeURIComponent(f.id)}`} target="_blank" rel="noreferrer">Open Drive</a>
          {f.error && <p role="status">{f.error}</p>}
        </div>)}
      </div>
    </>}
    <p>Existing imports and held jobs stay as they are. Sync adds new files; editing or deleting a Drive file does not replace or delete its imported copy.</p>
  </section>
}
