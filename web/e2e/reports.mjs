// Browser smoke test with a local stand-in API; no live accounts, database, or AI calls.
// Start Vite first, then: BASE_URL=http://127.0.0.1:5177 node e2e/reports.mjs [screenshots-directory]
import { chromium, expect } from '@playwright/test'
import { mkdirSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const base = process.env.BASE_URL ?? 'http://127.0.0.1:5177'
const out = process.argv[2] ?? mkdtempSync(join(tmpdir(), 'drivealt-reports-'))
mkdirSync(out, { recursive: true })
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined) })
const fileId = '01900000-0000-0000-0000-000000000001'
const pageId = '01900000-0000-0000-0000-000000000002'
const regionId = '01900000-0000-0000-0000-000000000003'
const me = { id: 'test-user', uniId: '24-0161', email: 'test@example.invalid', initials: 'AA', role: 'uploader' }
const file = { id: fileId, title: 'Data Structures Midterm', originalName: 'midterm.jpg', sourceKind: 'images', sizeBytes: 1000,
  pageCount: 1, parts: 1, status: 'ready', courseId: 1, courseSlug: 'data-structures', courseName: 'Data Structures',
  courseColor: '#2F6BFF', materialType: 'past_paper', year: 2024, guessed: [], thumbUrl: '/api/test-image' }
const card = { ref: `r:${regionId}`, kind: 'question', imageUrl: '/api/test-image', pageImageUrl: '/api/test-image',
  thumbUrl: '/api/test-image', fileId, pageId, regionId, fileTitle: file.title, fileName: file.originalName,
  sourceKind: 'images', courseSlug: 'data-structures', courseName: 'Data Structures', courseColor: '#2F6BFF',
  semester: 1, materialType: 'past_paper', year: 2024, pageNumber: 1, pageCount: 1, questionNumber: '1',
  tags: ['search'], box: { x: .05, y: .1, w: .9, h: .5 }, width: 600, height: 800, saved: true }
const picture = '<svg xmlns="http://www.w3.org/2000/svg" width="600" height="800"><rect width="600" height="800" fill="#fff9ec"/><text x="35" y="65" font-size="25">Data Structures Midterm</text><text x="35" y="130" font-size="20">Question 1: Compare two search methods.</text></svg>'
try {
  for (const [name, viewport] of [['desktop', { width: 1440, height: 1000 }], ['phone', { width: 390, height: 844 }]]) {
    const context = await browser.newContext({ viewport })
    const page = await context.newPage()
    const errors = []
    const reports = []
    const submissions = []
    page.on('pageerror', e => errors.push(e.message))
    await page.route('**/api/**', async route => {
      const req = route.request(), url = new URL(req.url()), path = url.pathname
      const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
      if (path === '/api/auth/me') return json(me)
      if (path === '/api/config') return json({ name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: false })
      if (path === '/api/bookmarks') return json([{ course: 'data-structures', name: 'Data Structures', color: '#2F6BFF', items: [card] }])
      if (path === '/api/admin/users') return json([{ ...me, pinned: true, lastLoginAt: null }])
      if (path === '/api/admin/stats') return json({ files: 1, ready: 1, processing: 0, failed: 0, pages: 1, questions: 1, users: 1 })
      if (path === `/api/files/${fileId}`) return json({ file, pages: [{ id: pageId, number: 1, width: 600, height: 800 }] })
      if (path.endsWith('/view') || path === '/api/test-image') return route.fulfill({ contentType: 'image/svg+xml', body: picture })
      if (path === '/api/reports' && req.method() === 'POST') {
        const body = req.postDataJSON()
        submissions.push(body)
        const report = { id: `report-${reports.length}`, ...body, status: 'open', createdAt: new Date().toISOString(),
          reporter: me.uniId, fileId, fileTitle: file.title, courseName: 'Data Structures', pageNumber: 1 }
        reports.push(report)
        return json({ id: report.id, alreadyReported: false })
      }
      if (path === '/api/reports') {
        const items = reports.filter(r => r.status === (url.searchParams.get('status') ?? 'open'))
        return json({ items, total: items.length, page: 1, pageSize: 30 })
      }
      if (path.endsWith('/resolve')) {
        const report = reports.find(r => path === `/api/reports/${r.id}/resolve`)
        if (!report) throw new Error('Unknown report')
        report.status = 'resolved'
        return json(report)
      }
      throw new Error(`Unexpected API request ${req.method()} ${path}`)
    })
    await page.goto(`${base}/saved`)
    await page.locator('.saved-card .shot').click()
    await page.getByRole('button', { name: 'Report a problem', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: 'Report a problem', exact: true })
    await expect(dialog).toBeVisible()
    await dialog.getByRole('combobox').selectOption('clipped_crop')
    await dialog.getByRole('textbox').fill('The crop clips the last line.')
    await dialog.getByRole('textbox').press('ArrowRight')
    await expect(page.locator('.viewer')).toBeVisible()
    await page.screenshot({ path: join(out, `${name}-report-form.png`) })
    await dialog.getByRole('button', { name: 'Send report' }).click()
    await expect(dialog).not.toBeVisible()
    expect(submissions[0].ref).toBe(`r:${regionId}`)
    await page.keyboard.press('Escape')
    await expect(page.locator('.viewer')).not.toBeVisible()

    await page.goto(`${base}/admin`)
    const inbox = page.getByRole('region', { name: 'Material reports' })
    await expect(inbox.getByText('The crop clips the last line.')).toBeVisible()
    await expect(inbox.getByRole('link', { name: 'Inspect & fix' })).toHaveAttribute('href', `/inspect/${fileId}?page=1`)
    await page.screenshot({ path: join(out, `${name}-reports-inbox.png`), fullPage: true })
    await inbox.getByRole('button', { name: 'Mark resolved' }).click()
    await expect(inbox.getByText(/No open reports/)).toBeVisible()
    await inbox.getByRole('button', { name: 'Resolved', exact: true }).click()
    await expect(inbox.getByText('The crop clips the last line.')).toBeVisible()

    await page.goto(`${base}/files/${fileId}?page=1`)
    await page.getByRole('button', { name: 'Report a problem', exact: true }).click()
    const pageReport = page.getByRole('dialog', { name: 'Report a problem', exact: true })
    await pageReport.getByRole('combobox').selectOption('missing_page')
    await pageReport.getByRole('button', { name: 'Send report' }).click()
    await expect(pageReport).not.toBeVisible()
    expect(submissions[1].ref).toBe(`p:${pageId}`)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
    expect(errors).toEqual([])
    await context.close()
    console.log(`${name}: crop report, review, resolve, history and page report passed`)
  }
  console.log(`Screenshots: ${out}`)
} finally { await browser.close() }
