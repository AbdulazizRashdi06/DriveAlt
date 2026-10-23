import { describe, expect, it } from 'vitest'
import { formatUniId } from './Login'

describe('formatUniId', () => {
  it('adds the dash while typing', () => {
    expect(formatUniId('2')).toBe('2')
    expect(formatUniId('24')).toBe('24-')
    expect(formatUniId('24-')).toBe('24-')
    expect(formatUniId('24--')).toBe('24-')
    expect(formatUniId('240161')).toBe('24-0161')
    expect(formatUniId('24 0161')).toBe('24-0161')
    expect(formatUniId('24-01619')).toBe('24-0161')
  })

  it('lets Backspace remove the dash', () => {
    expect(formatUniId('24', true)).toBe('24')
    expect(formatUniId('24-0', true)).toBe('24-0')
  })
})
