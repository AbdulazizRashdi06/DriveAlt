// Browser smoke test for "Continue as a guest" with a local stand-in API; no live accounts, database, or AI calls.
// Start Vite first, then: BASE_URL=http://127.0.0.1:5177 node e2e/guest.mjs [screenshots-directory]
import { chromium, expect } from '@playwright/test'
import { mkdirSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const base = process.env.BASE_URL ?? 'http://127.0.0.1:5177'
const out = process.argv[2] ?? mkdtempSync(join(tmpdir(), 'drivealt-guest-'))
mkdirSync(out, { recursive: true })
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined) })
const guest = { id: 'guest-id', uniId: 'Guest', email: '', initials: 'G', role: 'guest' }
const courses = [{ id: 1, slug: 'data-structures', name: 'Data Structures', semester: 1, group: 'CS & CYS', color: '#2F6BFF', aliases: [], files: 3 }]

try {
  for (const [name, viewport] of [['desktop', { width: 1440, height: 1000 }], ['phone', { width: 390, height: 844 }]]) {
    for (const guestChat of [true, false]) {
      const context = await browser.newContext({ viewport })
      const page = await context.newPage()
      const errors = []
      let me = null
      let bookmarkCalls = 0
      page.on('pageerror', e => errors.push(e.message))
      await page.route('**/api/**', async route => {
        const req = route.request(), path = new URL(req.url()).pathname
        const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
        if (path === '/api/config') return json({ name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: true, guestAccess: true, guestChat })
        if (path === '/api/auth/me') return me ? json(me) : route.fulfill({ status: 204 })
        if (path === '/api/auth/guest' && req.method() === 'POST') { me = guest; return json(guest) }
        if (path === '/api/bookmarks') { bookmarkCalls++; return json({ error: 'forbidden' }, 403) }
        if (path === '/api/courses') return json(courses)
        if (path === '/api/chats') return json([])
        return json({ error: `unexpected ${path}` }, 404)
      })

      await page.goto(`${base}/saved`)
      await expect(page.getByRole('button', { name: 'Continue as a guest' })).toBeVisible()
      await page.screenshot({ path: join(out, `${name}-login.png`), fullPage: true })
      await page.getByRole('button', { name: 'Continue as a guest' }).click()
      // Back where the visitor was headed, as a guest: Saved asks them to sign in instead of loading bookmarks.
      await expect(page.getByText('Saving is for signed-in students', { exact: false })).toBeVisible()
      if (bookmarkCalls !== 0) throw new Error('Saved loaded bookmarks for a guest')
      await expect(page.getByRole('link', { name: 'Sign in', exact: true })).toBeVisible()
      const chatTabs = await page.getByRole('link', { name: 'Chat', exact: true }).count()
      if (guestChat ? chatTabs === 0 : chatTabs !== 0) throw new Error(`Chat tab shown=${chatTabs > 0} with guestChat=${guestChat}`)
      if (await page.getByRole('link', { name: 'Upload' }).count()) throw new Error('Upload tab shown to a guest')
      await page.screenshot({ path: join(out, `${name}-${guestChat ? 'chat' : 'nochat'}-saved.png`), fullPage: true })

      if (!guestChat) {
        await page.goto(`${base}/chat`)
        await expect(page).toHaveURL(/\/browse$/)
      }
      await page.getByRole('link', { name: 'Sign in', exact: true }).click()
      await expect(page.getByRole('button', { name: 'Keep going as a guest' })).toBeVisible()
      await expect(page.getByLabel('Uni ID')).toBeVisible()
      if (errors.length) throw new Error(errors.join('\n'))
      await context.close()
      console.log(`${name} guestChat=${guestChat}: ok`)
    }
  }
  console.log(`screenshots: ${out}`)
} finally {
  await browser.close()
}
