import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../lib/api'
import { useApp } from '../lib/app'
import { ReportsInbox } from '../ui/ReportsInbox'

interface UserRow { id: string; uniId: string; email: string; role: 'student' | 'uploader'; lastLoginAt: string | null; pinned: boolean }
interface Stats { files: number; ready: number; processing: number; failed: number; pages: number; questions: number; users: number }

export function Admin() {
  const { me, toast } = useApp()
  const qc = useQueryClient()
  const users = useQuery({ queryKey: ['admin', 'users'], queryFn: () => api.get<UserRow[]>('/api/admin/users') })
  const stats = useQuery({ queryKey: ['admin', 'stats'], queryFn: () => api.get<Stats>('/api/admin/stats') })

  const setRole = async (u: UserRow, role: UserRow['role']) => {
    try {
      await api.put(`/api/admin/users/${u.id}/role`, { role })
      toast(`${u.uniId} is now ${role === 'uploader' ? 'an uploader' : 'a student'}.`)
      void qc.invalidateQueries({ queryKey: ['admin', 'users'] })
    } catch (e) { toast((e as Error).message) }
  }

  const s = stats.data
  return (
    <main className="page-main">
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <h1 className="disp" style={{ margin: 0, fontSize: 44, lineHeight: 1 }}>People & reports</h1>
        <span style={{ font: "800 16px/1.4 'Nunito', sans-serif", color: '#7A5A3A' }}>Uploaders can upload and inspect. Everyone in the batch can search, browse and save.</span>
      </div>
      {s && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))', gap: 12 }}>
          {[['Files ready', s.ready], ['Processing', s.processing], ['Failed', s.failed], ['Pages', s.pages], ['Question crops', s.questions], ['People', s.users]].map(([k, v]) => (
            <div key={k} style={{ padding: '16px 18px', borderRadius: 22, background: '#FFF9EC', borderBottom: '4px solid #F3E6CB' }}>
              <div className="lbl">{k}</div>
              <div className="disp" style={{ fontSize: 32 }}>{v}</div>
            </div>
          ))}
        </div>
      )}
      <ReportsInbox />
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <h2 className="disp" style={{ margin: 0 }}>People & roles</h2>
        {users.data?.map(u => (
          <div key={u.id} className="row" style={{ padding: '12px 16px', gap: 12 }}>
            <div className="fname">
              <b>{u.uniId}{u.id === me?.id ? ' (you)' : ''}</b>
              <span>{u.email}{u.lastLoginAt ? ` · last seen ${new Date(u.lastLoginAt).toLocaleDateString()}` : ''}</span>
            </div>
            <div role="radiogroup" aria-label={`Role for ${u.uniId}`} className="segs">
              {(['student', 'uploader'] as const).map(r => (
                <button key={r} type="button" role="radio" aria-checked={u.role === r} className={`seg ${u.role === r ? 'on' : ''}`}
                  disabled={u.pinned || u.id === me?.id} onClick={() => void setRole(u, r)}>{r === 'student' ? 'Student' : 'Uploader'}</button>
              ))}
            </div>
            {u.pinned && <span className="lbl">set in server config</span>}
          </div>
        ))}
      </div>
    </main>
  )
}
