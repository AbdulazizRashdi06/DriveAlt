/** Normalized (0..1) box math for the region editor. */
export interface NBox { x: number; y: number; w: number; h: number }
export type Handle = 'nw' | 'ne' | 'sw' | 'se'

export const MIN_SIZE = 0.01

const clamp01 = (v: number) => Math.min(1, Math.max(0, v))

/** Keeps a box inside the page and at least MIN_SIZE big. */
export function clampBox(b: NBox): NBox {
  const w = Math.min(1, Math.max(MIN_SIZE, b.w))
  const h = Math.min(1, Math.max(MIN_SIZE, b.h))
  return { x: Math.min(Math.max(0, b.x), 1 - w), y: Math.min(Math.max(0, b.y), 1 - h), w, h }
}

export function moveBox(b: NBox, dx: number, dy: number): NBox {
  return clampBox({ ...b, x: b.x + dx, y: b.y + dy })
}

/** Drags one corner; the opposite corner stays put. Flipping past it is allowed and normalized. */
export function resizeBox(b: NBox, handle: Handle, px: number, py: number): NBox {
  const x = clamp01(px)
  const y = clamp01(py)
  let x0 = b.x, y0 = b.y, x1 = b.x + b.w, y1 = b.y + b.h
  if (handle === 'nw' || handle === 'sw') x0 = x; else x1 = x
  if (handle === 'nw' || handle === 'ne') y0 = y; else y1 = y
  return clampBox(fromCorners(x0, y0, x1, y1))
}

export function fromCorners(ax: number, ay: number, bx: number, by: number): NBox {
  const x0 = clamp01(Math.min(ax, bx)), x1 = clamp01(Math.max(ax, bx))
  const y0 = clamp01(Math.min(ay, by)), y1 = clamp01(Math.max(ay, by))
  return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 }
}

/** A drawn box is kept only if it's big enough to mean something. */
export function isUsable(b: NBox) {
  return b.w >= MIN_SIZE * 1.5 && b.h >= MIN_SIZE * 1.5
}

/** Pointer position relative to an element, normalized to 0..1. */
export function relPoint(el: Element, clientX: number, clientY: number) {
  const r = el.getBoundingClientRect()
  return { x: clamp01((clientX - r.left) / r.width), y: clamp01((clientY - r.top) / r.height) }
}

/** Index after stepping through a list, clamped to its ends. */
export function step(index: number, delta: number, length: number) {
  return Math.min(Math.max(index + delta, 0), Math.max(length - 1, 0))
}
