// Stand-in API layout test: a long chat history scrolls inside the sidebar instead of stretching the page.
// Start Vite on :5177, then: node e2e/chat-history.mjs [screenshots-directory]
import { chromium, expect } from '@playwright/test'
import { mkdtempSync, mkdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const base = process.env.BASE_URL ?? 'http://127.0.0.1:5177'
const out = process.argv[2] ?? mkdtempSync(join(tmpdir(), 'drivealt-chat-history-'))
mkdirSync(out, { recursive: true })
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined) })
const me = { id: 'test-user', uniId: '25-1000', initials: 'ST', role: 'student' }
const many = Array.from({ length: 40 }, (_, i) => ({ id: `chat-${i}`, title: `Past paper questions on topic ${i + 1}`, updatedAt: new Date().toISOString() }))
let chats = many
try {
  for (const [name, viewport] of [['desktop', { width: 1440, height: 900 }], ['laptop', { width: 1280, height: 720 }], ['phone', { width: 390, height: 844 }]]) {
    const context = await browser.newContext({ viewport })
    const page = await context.newPage()
    const errors = []
    page.on('pageerror', e => { errors.push(e.message); console.error(`${name} page error: ${e.message}`) })
    await page.route('**/api/**', async route => {
      const path = new URL(route.request().url()).pathname
      const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
      if (path === '/api/auth/me') return json(me)
      if (path === '/api/config') return json({ name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: false })
      if (path === '/api/chats') return json(chats)
      throw new Error(`Unexpected API request ${path}`)
    })
    // Baseline: how tall the page is with a single chat, so the check below isolates the history's effect.
    chats = many.slice(0, 1)
    await page.goto(`${base}/chat`)
    await expect(page.getByRole('heading', { name: 'What should I dig up?' })).toBeVisible()
    await page.waitForLoadState('networkidle')
    const baseline = await page.evaluate(() => document.documentElement.scrollHeight)
    chats = many
    await page.goto(`${base}/chat`)
    await expect(page.getByRole('heading', { name: 'What should I dig up?' })).toBeVisible()
    if (name === 'phone') {
      await page.getByRole('button', { name: 'Your chats' }).click()
      const drawer = page.locator('aside.drawer')
      await expect(drawer.locator('.hist')).toHaveCount(chats.length)
      await expect(drawer.getByRole('link', { name: 'New chat' })).toBeVisible()
      await drawer.locator('.hist').last().scrollIntoViewIfNeeded()
      await expect(drawer.locator('.hist').last()).toBeInViewport()
    } else {
      const side = page.locator('aside.chat-side')
      await expect(side.locator('.hist')).toHaveCount(chats.length)
      // The page must not grow to fit the history, and the sidebar must fit inside the viewport.
      expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBeLessThanOrEqual(Math.max(baseline, viewport.height) + 1)
      const box = await side.boundingBox()
      expect(box.height).toBeLessThanOrEqual(viewport.height)
      // The list scrolls on its own and every chat stays reachable.
      const list = side.locator('.hist-list')
      expect(await list.evaluate(el => el.scrollHeight > el.clientHeight)).toBe(true)
      await side.locator('.hist').last().scrollIntoViewIfNeeded()
      await expect(side.locator('.hist').last()).toBeInViewport()
      await expect(side.getByRole('link', { name: 'New chat' })).toBeInViewport()
    }
    await page.screenshot({ path: join(out, `${name}-chat-history.png`) })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
    expect(errors).toEqual([])
    await context.close()
    console.log(`${name}: long chat history stays inside the sidebar`)
  }
  console.log(`Screenshots: ${out}`)
} finally { await browser.close() }
