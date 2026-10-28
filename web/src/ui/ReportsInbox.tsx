import { useState } from 'react'
import { Link } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../lib/api'
import { useApp } from '../lib/app'
import { REPORT_REASONS } from './ReportButton'

interface Report {
  id: string; reason: keyof typeof REPORT_REASONS; details: string; status: string; createdAt: string
  reporter: string; fileId: string; fileTitle: string; courseName: string; pageNumber: number
}
interface ReportPage { items: Report[]; total: number; page: number; pageSize: number }

export function ReportsInbox() {
  const [status, setStatus] = useState<'open' | 'resolved'>('open')
  const [page, setPage] = useState(1)
  const [busy, setBusy] = useState<string | null>(null)
  const { toast } = useApp()
  const qc = useQueryClient()
  const reports = useQuery({ queryKey: ['reports', status, page], queryFn: () => api.get<ReportPage>(`/api/reports?status=${status}&page=${page}`) })

  const resolve = async (id: string) => {
    setBusy(id)
    try {
      await api.post(`/api/reports/${id}/resolve`)
      setPage(1)
      await qc.invalidateQueries({ queryKey: ['reports'] })
      toast('Report marked as resolved.')
    } catch (e) { toast((e as Error).message) }
    finally { setBusy(null) }
  }

  return <section className="reports-inbox" aria-labelledby="reports-title">
    <div className="report-title">
      <h2 id="reports-title" className="disp">Material reports</h2>
      <div className="segs" aria-label="Report status">
        {(['open', 'resolved'] as const).map(s => <button className={`seg ${status === s ? 'on' : ''}`} type="button" key={s} aria-pressed={status === s}
          onClick={() => { setStatus(s); setPage(1) }}>{s === 'open' ? 'Open' : 'Resolved'}</button>)}
      </div>
    </div>
    {reports.isPending && <p role="status">Loading reports…</p>}
    {reports.isError && <div className="err" role="alert">Couldn't load reports. <button type="button" onClick={() => void reports.refetch()}>Try again</button></div>}
    {reports.data?.total === 0 && <p>No {status} reports. {status === 'open' && 'Students can report a problem from any page or result.'}</p>}
    {reports.data?.items.map(r => <article className="report-row" key={r.id}>
      <div className="lbl">{REPORT_REASONS[r.reason] ?? 'Reported problem'}</div>
      <h3>{r.fileTitle} · page {r.pageNumber}</h3>
      <div className="report-source">{r.courseName} · {r.reporter} · {new Date(r.createdAt).toLocaleDateString()}</div>
      {r.details && <p className="report-details">{r.details}</p>}
      <div className="report-actions">
        <Link className="candy c-white sm" to={`/files/${r.fileId}?page=${r.pageNumber}`}>Open page</Link>
        <Link className="candy c-cream sm" to={`/inspect/${r.fileId}?page=${r.pageNumber}`}>Inspect & fix</Link>
        {status === 'open' && <button className="candy c-green sm" type="button" disabled={busy !== null} onClick={() => void resolve(r.id)}>
          {busy === r.id ? 'Saving…' : 'Mark resolved'}
        </button>}
      </div>
    </article>)}
    {reports.data && reports.data.total > reports.data.pageSize && <div className="report-actions">
      <button className="candy c-white sm" type="button" disabled={page === 1} onClick={() => setPage(p => p - 1)}>Previous</button>
      <span className="lbl">Page {page} of {Math.ceil(reports.data.total / reports.data.pageSize)}</span>
      <button className="candy c-white sm" type="button" disabled={page * reports.data.pageSize >= reports.data.total} onClick={() => setPage(p => p + 1)}>Next</button>
    </div>}
  </section>
}
