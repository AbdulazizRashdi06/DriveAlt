import { describe, expect, it } from 'vitest'
import { waitText } from './Upload'

describe('Upload wait reasons', () => {
  it('explains each server reason in plain words', () => {
    expect(waitText({ reason: 'reading' })).toMatch(/reading it now/)
    expect(waitText({ reason: 'queued', ahead: 0 })).toBe('Next in line.')
    expect(waitText({ reason: 'queued', ahead: 3 })).toBe('Waiting in line: 3 files ahead.')
    expect(waitText({ reason: 'paused', detail: 'Codex unavailable (429)', until: '2026-10-31T10:00:00Z' })).toMatch(/usage limit was reached\. Resumes about/)
    expect(waitText({ reason: 'paused', detail: 'Codex is not signed in', until: '2026-10-31T10:00:00Z' })).toMatch(/isn't available right now/)
    expect(waitText({ reason: 'retrying', attempt: 2, detail: 'Incomplete extraction batch.', until: '2026-10-31T10:00:00Z' })).toMatch(/attempt 2 of 3\): Incomplete extraction batch\./)
    expect(waitText({ reason: 'held', detail: 'Held: owner scope' })).toBe('On hold: owner scope. Press Resume to process it now.')
    expect(waitText({ reason: 'stuck' })).toMatch(/Press Retry/)
    expect(waitText(null)).toBeNull()
  })
})
