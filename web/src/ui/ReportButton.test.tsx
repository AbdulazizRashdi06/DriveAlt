import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { api } from '../lib/api'
import { ReportButton } from './ReportButton'

const { toast } = vi.hoisted(() => ({ toast: vi.fn() }))
vi.mock('../lib/app', () => ({ useApp: () => ({ toast }) }))
vi.mock('../lib/api', () => ({ api: { post: vi.fn() } }))

const target = { ref: 'r:01900000-0000-0000-0000-000000000001', fileTitle: 'Data Structures Midterm', pageNumber: 3 }

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
})
beforeEach(() => { vi.mocked(api.post).mockReset(); toast.mockReset() })
afterEach(cleanup)

describe('Report a problem', () => {
  it('sends the exact crop reference and optional note, then confirms and closes', async () => {
    vi.mocked(api.post).mockResolvedValue({ alreadyReported: false })
    render(<ReportButton target={target} />)
    fireEvent.click(screen.getByRole('button', { name: 'Report a problem' }))
    expect(screen.getByText('Data Structures Midterm · page 3')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Send report' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.change(screen.getByRole('combobox', { name: "What's wrong?" }), { target: { value: 'clipped_crop' } })
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'The last line is cut off.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Send report' }))
    await waitFor(() => expect(api.post).toHaveBeenCalledWith('/api/reports', { ref: target.ref, reason: 'clipped_crop', details: 'The last line is cut off.' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(toast).toHaveBeenCalledWith('Report sent. Thanks for helping fix the drive!')
  })

  it('preserves the form on a failed request so the student can retry', async () => {
    vi.mocked(api.post).mockRejectedValueOnce(new Error('Connection lost. Please try again.')).mockResolvedValueOnce({ alreadyReported: true })
    render(<ReportButton target={target} />)
    fireEvent.click(screen.getByRole('button', { name: 'Report a problem' }))
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'wrong_course' } })
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Wrong label' } })
    fireEvent.click(screen.getByRole('button', { name: 'Send report' }))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe('Connection lost. Please try again.'))
    expect((screen.getByRole('textbox') as HTMLTextAreaElement).value).toBe('Wrong label')
    fireEvent.click(screen.getByRole('button', { name: 'Send report' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(toast).toHaveBeenCalledWith('This problem is already reported. Thanks!')
  })

  it('keeps report keyboard events inside the dialog and restores focus on cancel', async () => {
    render(<ReportButton target={target} />)
    const trigger = screen.getByRole('button', { name: 'Report a problem' })
    trigger.focus()
    fireEvent.click(trigger)
    const listener = vi.fn()
    window.addEventListener('keydown', listener)
    try {
      fireEvent.keyDown(screen.getByRole('combobox'), { key: 'ArrowRight' })
      fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Escape' })
      expect(listener).not.toHaveBeenCalled()
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
      expect(screen.queryByRole('dialog')).toBeNull()
      await waitFor(() => expect(document.activeElement).toBe(trigger))
      expect(api.post).not.toHaveBeenCalled()
    } finally { window.removeEventListener('keydown', listener) }
  })
})
