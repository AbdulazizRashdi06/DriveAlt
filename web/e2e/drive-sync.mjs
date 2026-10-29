// Local stand-in API: checks folder switches and layout without importing or paying for AI.
import { chromium, expect } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
const base = process.env.BASE_URL ?? 'http://127.0.0.1:5177'
const out = mkdtempSync(join(tmpdir(), 'drivealt-sync-'))
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined) })
try {
  for (const [name, viewport] of [['desktop', { width: 1440, height: 1000 }], ['phone', { width: 390, height: 844 }]]) {
    const context = await browser.newContext({ viewport })
    const page = await context.newPage()
    const errors = [], changes = []
    const state = { enabled: false, scanRequested: false, startedAt: null, finishedAt: null, error: null, imported: 0,
      folders: ['semester 1/Data Structures/past papers', 'semester 1/AI/ITS', 'semester 2/CS/Efficient Algorithms/slides'].map((p, i) => ({ id: `${i}`, name: p.split('/').at(-1), segments: p.split('/'), enabled: false, available: true })) }
    page.on('pageerror', e => errors.push(e.message))
    await page.route('**/api/**', async route => {
      const req = route.request(), path = new URL(req.url()).pathname
      const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
      if (path === '/api/auth/me') return json({ id: 'uploader', uniId: '24-0161', role: 'uploader', email: 'test@example.invalid', initials: 'AA' })
      if (path === '/api/config') return json({ name: 'DriveAlt', aiOnline: false })
      if (path === '/api/admin/users') return json([])
      if (path === '/api/admin/stats') return json({ files: 403, ready: 212, processing: 190, failed: 1, pages: 9673, questions: 1105, users: 1 })
      if (path === '/api/reports') return json({ items: [], total: 0, page: 1, pageSize: 30 })
      if (path.startsWith('/api/admin/drive-sync')) {
        if (req.method() === 'PUT') {
          changes.push({ path, ...req.postDataJSON() })
          if (path.includes('/folders/')) state.folders.find(f => f.id === path.split('/').at(-1)).enabled = req.postDataJSON().enabled
          else state.enabled = req.postDataJSON().enabled
        }
        return json(state)
      }
      return json([])
    })
    await page.goto(`${base}/admin`)
    const ds = page.getByRole('switch', { name: 'Sync semester 1 / Data Structures / past papers', exact: true })
    await expect(ds).not.toBeChecked()
    await ds.click()
    await expect(ds).toBeChecked()
    await expect(page.getByRole('switch', { name: 'Sync semester 1 / AI / ITS', exact: true })).not.toBeChecked()
    await expect(page.getByRole('switch', { name: 'Sync semester 2 / CS / Efficient Algorithms / slides', exact: true })).not.toBeChecked()
    await expect(page.getByRole('switch', { name: 'Automatic Drive sync' })).not.toBeChecked()
    state.startedAt = new Date().toISOString()
    state.folders[0].checkedAt = new Date(Date.now() + 1000).toISOString()
    await expect(page.getByText('1 of 3 known folders checked · 0 file groups imported')).toBeVisible({ timeout: 15000 })
    await expect(page.getByRole('progressbar', { name: 'Drive folder scan progress' })).toHaveJSProperty('value', 1)
    await expect(page.getByRole('progressbar')).toHaveJSProperty('max', 3)
    await page.getByRole('heading', { name: 'Google Drive sync' }).scrollIntoViewIfNeeded()
    await page.screenshot({ path: join(out, `${name}.png`), fullPage: true })
    if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw new Error(`${name}: horizontal overflow`)
    if (errors.length) throw new Error(errors.join('\n'))
    if (changes.length !== 1 || !changes[0].path.endsWith('/folders/0')) throw new Error('Unexpected folder changes')
    await context.close()
  }
  console.log(`Drive sync desktop/phone checks passed. Screenshots: ${out}`)
} finally { await browser.close() }
