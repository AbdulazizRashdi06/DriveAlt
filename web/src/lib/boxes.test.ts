import { describe, expect, it } from 'vitest'
import { clampBox, fromCorners, isUsable, moveBox, resizeBox, step } from './boxes'

describe('region box math', () => {
  it('keeps moved boxes inside the page', () => {
    expect(moveBox({ x: 0.8, y: 0.8, w: 0.3, h: 0.3 }, 0.2, 0.2)).toEqual({ x: 0.7, y: 0.7, w: 0.3, h: 0.3 })
    expect(moveBox({ x: 0.1, y: 0.1, w: 0.2, h: 0.2 }, -0.5, -0.5)).toEqual({ x: 0, y: 0, w: 0.2, h: 0.2 })
  })

  it('resizes from a corner and keeps the opposite corner fixed', () => {
    const b = resizeBox({ x: 0.2, y: 0.2, w: 0.4, h: 0.4 }, 'se', 0.9, 0.7)
    expect(b.x).toBeCloseTo(0.2); expect(b.y).toBeCloseTo(0.2)
    expect(b.w).toBeCloseTo(0.7); expect(b.h).toBeCloseTo(0.5)
  })

  it('normalizes a corner dragged past the opposite one', () => {
    const b = resizeBox({ x: 0.4, y: 0.4, w: 0.2, h: 0.2 }, 'nw', 0.8, 0.9)
    expect(b.x).toBeCloseTo(0.6); expect(b.y).toBeCloseTo(0.6)
    expect(b.w).toBeCloseTo(0.2); expect(b.h).toBeCloseTo(0.3)
  })

  it('builds boxes from any two corners and drops tiny ones', () => {
    expect(fromCorners(0.5, 0.6, 0.1, 0.2)).toEqual({ x: 0.1, y: 0.2, w: 0.4, h: expect.closeTo(0.4) })
    expect(isUsable(fromCorners(0.1, 0.1, 0.105, 0.3))).toBe(false)
    expect(isUsable(fromCorners(0.1, 0.1, 0.3, 0.3))).toBe(true)
  })

  it('enforces a minimum size', () => {
    expect(clampBox({ x: 0.5, y: 0.5, w: 0, h: 0 }).w).toBeGreaterThan(0)
  })

  it('steps through results without running off the ends', () => {
    expect(step(0, -1, 3)).toBe(0)
    expect(step(2, 1, 3)).toBe(2)
    expect(step(1, 1, 3)).toBe(2)
    expect(step(0, 1, 0)).toBe(0)
  })
})
