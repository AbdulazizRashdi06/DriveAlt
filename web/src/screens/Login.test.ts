import { describe, expect, it } from 'vitest'
import { formatUniId } from './Login'

describe('formatUniId', () => {
  it('adds the dash while typing', () => {
    expect(formatUniId('24')).toBe('24')
    expect(formatUniId('240161')).toBe('24-0161')
    expect(formatUniId('24 0161')).toBe('24-0161')
    expect(formatUniId('24-01619')).toBe('24-0161')
  })
})
