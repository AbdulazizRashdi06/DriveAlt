import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { api } from '../lib/api'
import { useApp } from '../lib/app'
import { IconClose, IconFlag } from './Icons'

export const REPORT_REASONS = {
  clipped_crop: 'Clipped question crop',
  wrong_course: 'Wrong course or label',
  missing_page: 'Page missing or unreadable',
  irrelevant_result: 'Irrelevant search result',
  other: 'Something else',
} as const

export interface ReportTarget { ref: string; fileTitle: string; pageNumber: number }

export function ReportButton({ target, compact = false }: { target: ReportTarget; compact?: boolean }) {
  const [open, setOpen] = useState(false)
  const trigger = useRef<HTMLButtonElement>(null)
  const close = () => {
    setOpen(false)
    // Wait for the modal to leave the top layer before returning focus to its trigger.
    window.setTimeout(() => trigger.current?.focus(), 0)
  }
  return <>
    <button ref={trigger} className="candy c-white sm" type="button" aria-label="Report a problem" title="Report a problem" onClick={() => setOpen(true)}>
      <IconFlag size={16} /><span className={compact ? 'hide-phone' : undefined}>Report a problem</span>
    </button>
    {open && <ReportForm target={target} onClose={close} />}
  </>
}

export function ReportForm({ target, onClose }: { target: ReportTarget; onClose: () => void }) {
  const { toast } = useApp()
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const [reason, setReason] = useState<keyof typeof REPORT_REASONS | ''>('')
  const [details, setDetails] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!reason || busy) return
    setBusy(true)
    setError(null)
    try {
      const result = await api.post<{ alreadyReported: boolean }>('/api/reports', { ref: target.ref, reason, details })
      toast(result.alreadyReported ? 'This problem is already reported. Thanks!' : 'Report sent. Thanks for helping fix the drive!')
      onClose()
    } catch (e) { setError((e as Error).message); setBusy(false) }
  }

  return (
    <dialog ref={dialog} className="report-dialog" aria-labelledby={titleId} onCancel={onClose} onKeyDown={e => e.stopPropagation()}>
      <form onSubmit={e => void submit(e)}>
        <div className="report-title">
          <h2 id={titleId} className="disp">Report a problem</h2>
          <button className="candy c-white rnd sm" type="button" aria-label="Close report" onClick={onClose}><IconClose /></button>
        </div>
        <p className="report-source">{target.fileTitle} · page {target.pageNumber}</p>
        <label className="field">
          <span className="lbl">What's wrong?</span>
          <select className="sel" required autoFocus value={reason} onChange={e => setReason(e.target.value as typeof reason)}>
            <option value="" disabled>Choose a reason</option>
            {Object.entries(REPORT_REASONS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </label>
        <label className="field">
          <span className="lbl">Anything else? <span className="report-optional">Optional</span></span>
          <textarea className="textarea" rows={3} maxLength={1000} value={details} onChange={e => setDetails(e.target.value)} placeholder="A short note helps us fix it." />
        </label>
        {error && <div className="err" role="alert">{error}</div>}
        <div className="report-actions">
          <button className="candy c-white" type="button" onClick={onClose}>Cancel</button>
          <button className="candy" type="submit" disabled={!reason || busy}>{busy ? 'Sending…' : 'Send report'}</button>
        </div>
      </form>
    </dialog>
  )
}
