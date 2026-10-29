// Browser smoke test for Features #5 (Instant Page-Strip Reader), #6 (Deep-Link Sharing), and #7 (Duplicate Preflight UI).
// Start Vite first, then: BASE_URL=http://127.0.0.1:5177 node e2e/features-567.mjs [screenshots-directory]
import { chromium, expect } from '@playwright/test'
import { mkdirSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const base = process.env.BASE_URL ?? 'http://127.0.0.1:5177'
const out = process.argv[2] ?? mkdtempSync(join(tmpdir(), 'drivealt-567-'))
mkdirSync(out, { recursive: true })
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined) })

const fileId = '01900000-0000-0000-0000-000000000001'
const page1Id = '01900000-0000-0000-0000-000000000002'
const page2Id = '01900000-0000-0000-0000-000000000003'
const regionId = '01900000-0000-0000-0000-000000000004'
const me = { id: 'test-user', uniId: '24-0161', email: 'test@example.invalid', initials: 'AA', role: 'uploader' }

const file = {
  id: fileId,
  title: 'Numerical Computation Midterm',
  originalName: 'MIDTERM Numerical computation SS2024.pdf',
  sourceKind: 'pdf',
  sizeBytes: 152000,
  pageCount: 2,
  parts: 1,
  status: 'ready',
  step: 5,
  progress: 2,
  error: null,
  courseId: 5,
  courseSlug: 'numerical-computation',
  courseName: 'Numerical Computation',
  courseColor: '#2F6BFF',
  materialType: 'past_paper',
  year: 2024,
  assessmentLabel: 'Midterm',
  folder: null,
  guessed: [],
  createdAt: '2026-10-28T00:00:00Z',
  thumbUrl: '/api/media/thumbs/' + page1Id + '?v=1',
}

const pages = [
  {
    id: page1Id,
    number: 1,
    kind: 'assessment',
    thumbUrl: `/api/media/thumbs/${page1Id}?v=1`,
    pageUrl: `/api/media/pages/${page1Id}?v=1`,
    tags: ['cramers rule', 'matrices'],
    questions: 1,
    saved: true,
    width: 600,
    height: 800,
    regions: [{ id: regionId, questionNumber: '1', x: 0.08, y: 0.18, w: 0.84, h: 0.36, saved: true }],
  },
  {
    id: page2Id,
    number: 2,
    kind: 'assessment',
    thumbUrl: `/api/media/thumbs/${page2Id}?v=1`,
    pageUrl: `/api/media/pages/${page2Id}?v=1`,
    tags: ['gaussian elimination'],
    questions: 0,
    width: 600,
    height: 800,
    regions: [],
  },
]

const card = {
  ref: `r:${regionId}`,
  kind: 'question',
  imageUrl: `/api/media/crops/${regionId}?v=1`,
  pageImageUrl: `/api/media/pages/${page1Id}?v=1`,
  thumbUrl: `/api/media/thumbs/${page1Id}?v=1`,
  fileId,
  pageId: page1Id,
  regionId,
  fileTitle: file.title,
  fileName: file.originalName,
  sourceKind: 'pdf',
  courseSlug: 'numerical-computation',
  courseName: 'Numerical Computation',
  courseColor: '#2F6BFF',
  semester: 2,
  materialType: 'past_paper',
  year: 2024,
  assessmentLabel: 'Midterm',
  pageNumber: 1,
  pageCount: 2,
  questionNumber: '1',
  tags: ['cramers rule'],
  box: { x: 0.08, y: 0.18, w: 0.84, h: 0.36 },
  width: 600,
  height: 800,
  saved: false,
  answerRef: null,
  answerImageUrl: null,
}

const svgPage = '<svg xmlns="http://www.w3.org/2000/svg" width="600" height="800"><rect width="600" height="800" fill="#fff9ec"/><text x="40" y="70" font-size="24" font-family="sans-serif">Numerical Computation Midterm</text><text x="40" y="200" font-size="18" font-family="sans-serif">Q1: Solve the 3x3 system using Cramer\'s Rule.</text></svg>'

try {
  for (const [name, viewport] of [['desktop', { width: 1440, height: 1000 }], ['phone', { width: 390, height: 844 }]]) {
    const context = await browser.newContext({ viewport })
    const page = await context.newPage()
    const errors = []
    let uploadedCount = 0
    page.on('pageerror', e => errors.push(e.message))

    await page.route('**/api/**', async route => {
      const req = route.request(), url = new URL(req.url()), path = url.pathname
      const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
      if (path === '/api/auth/me') return json(me)
      if (path === '/api/config') return json({ name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: true })
      if (path === `/api/files/${fileId}`) return json({ file, pages })
      if (path.startsWith('/api/media/')) return route.fulfill({ contentType: 'image/svg+xml', body: svgPage })
      if (path === '/api/cards') return json([card])
      if (path === '/api/courses') return json([{ id: 5, slug: 'numerical-computation', name: 'Numerical Computation', semester: 2, group: '', color: '#2F6BFF', aliases: [], files: 1 }])
      if (path === '/api/courses/numerical-computation') return json({ course: { id: 5, slug: 'numerical-computation', name: 'Numerical Computation', semester: 2, group: '', color: '#2F6BFF', aliases: [], files: 1 }, types: { past_paper: 1 }, folders: [], files: [file], tags: [], years: [2024] })
      if (path === '/api/uploads/check' && req.method() === 'POST') {
        return json([
          { index: 0, kind: 'exact', existing: file },
          { index: 1, kind: 'similar', existing: file },
        ])
      }
      if (path === '/api/uploads' && req.method() === 'POST') {
        uploadedCount++
        return json([{ duplicate: false, file }])
      }
      if (path === '/api/uploads') return json(uploadedCount > 0 ? [file] : [])
      throw new Error(`Unexpected API request ${req.method()} ${path}`)
    })

    // #5 Instant Page-Strip Reader
    await page.goto(`${base}/files/${fileId}?page=1`)
    await expect(page.getByRole('button', { name: 'Open Q1 on page 1' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Hide questions (1)' })).toBeVisible()
    await page.screenshot({ path: join(out, `${name}-file-reader.png`), animations: 'disabled' })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)

    await page.getByRole('button', { name: 'Open Q1 on page 1' }).click()
    await expect(page.locator('.viewer')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Saved', exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Copy link', exact: true })).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.locator('.viewer')).not.toBeVisible()

    await page.getByRole('button', { name: 'Zoom page 1' }).click()
    await expect(page.getByRole('button', { name: 'Saved', exact: true })).toBeVisible()
    await page.keyboard.press('Escape')

    // #6 Deep-Link Sharing (?ref=r:...)
    await page.goto(`${base}/browse/numerical-computation?ref=r%3A${regionId}`)
    await expect(page.locator('.viewer')).toBeVisible()
    await page.screenshot({ path: join(out, `${name}-deep-link-viewer.png`), animations: 'disabled' })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
    await page.keyboard.press('Escape')
    await expect(page.locator('.viewer')).not.toBeVisible()

    // #7 Pre-upload duplicate check UI
    await page.goto(`${base}/upload`)
    const fileInput = page.locator('input[type="file"][multiple]')
    await fileInput.setInputFiles([
      { name: 'MIDTERM Numerical computation SS2024.pdf', mimeType: 'application/pdf', buffer: Buffer.from('exact') },
      { name: 'MIDTERM Numerical computation SS2024 (1).pdf', mimeType: 'application/pdf', buffer: Buffer.from('similar') },
    ])
    await expect(page.getByRole('region', { name: 'Already in drive: MIDTERM Numerical computation SS2024.pdf' })).toBeVisible()
    await expect(page.getByRole('region', { name: 'Possible duplicate: MIDTERM Numerical computation SS2024 (1).pdf' })).toBeVisible()
    expect(uploadedCount).toBe(0)
    await page.screenshot({ path: join(out, `${name}-upload-dupcheck.png`), fullPage: true, animations: 'disabled' })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)

    await page.getByRole('button', { name: 'Upload anyway' }).click()
    await expect.poll(() => uploadedCount).toBe(1)

    expect(errors).toEqual([])
    await context.close()
    console.log(`${name}: instant reader, deep-link viewer, and duplicate check passed`)
  }
  console.log(`Screenshots: ${out}`)
} finally {
  await browser.close()
}
