// Stand-in API smoke test: full-file cards, show more, original-file navigation and topic crops.
// Start Vite on :5177, then: node e2e/chat-files.mjs [screenshots-directory]
import { chromium, expect } from '@playwright/test'
import { mkdtempSync, mkdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const base = process.env.BASE_URL ?? 'http://127.0.0.1:5177'
const out = process.argv[2] ?? mkdtempSync(join(tmpdir(), 'drivealt-chat-files-'))
mkdirSync(out, { recursive: true })
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL ?? (process.platform === 'win32' ? 'msedge' : undefined) })
const me = { id: 'test-user', uniId: '25-1000', initials: 'ST', role: 'student' }
const picture = '<svg xmlns="http://www.w3.org/2000/svg" width="600" height="800"><rect width="600" height="800" fill="#fff9ec"/><text x="35" y="65" font-size="25">Data Structures Midterm</text><text x="35" y="130" font-size="20">Question 1: Compare two search methods.</text></svg>'
const files = [1, 2, 3, 4].map(i => ({
  ref: `f:file-${i}`, kind: 'file', fileId: `file-${i}`, pageId: `page-${i}`, regionId: null,
  fileTitle: `Data Structures Midterm ${i}`, fileName: `midterm-${i}.jpg`, sourceKind: 'images',
  courseSlug: 'data-structures', courseName: 'Data Structures', courseColor: '#2F6BFF', semester: 1,
  materialType: 'past_paper', year: 2024, pageNumber: 1, pageCount: 2, tags: [],
  imageUrl: '/api/test-image', thumbUrl: '/api/test-image', pageImageUrl: '/api/test-image', width: 600, height: 800, saved: false,
}))
const message = (id, role, text, results = [], moreCount = 0) => ({ id, role, text, results, moreCount, refused: false, createdAt: new Date().toISOString() })
try {
  for (const [name, viewport] of [['desktop', { width: 1440, height: 1000 }], ['phone', { width: 390, height: 844 }]]) {
    const context = await browser.newContext({ viewport })
    const page = await context.newPage()
    const errors = []
    page.on('pageerror', e => { errors.push(e.message); console.error(`${name} page error: ${e.message}`) })
    const messages = [message('u1', 'user', 'ok i want full ds past papers'),
      message('b1', 'bot', 'Here are 4 full files for Data Structures. Open or download the whole file below.', files.slice(0, 3), 1)]
    const conversation = { id: 'test-chat', title: 'Full DS past papers', updatedAt: new Date().toISOString() }
    await page.route('**/api/**', async route => {
      const req = route.request(), path = new URL(req.url()).pathname
      const json = body => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) })
      if (path === '/api/auth/me') return json(me)
      if (path === '/api/config') return json({ name: 'DriveAlt', emailDomain: '@student.gutech.edu.om', aiOnline: false })
      if (path === '/api/chats') return json([conversation])
      if (path === '/api/chats/test-chat') return json({ conversation, messages })
      if (path === '/api/chats/test-chat/messages') {
        const body = req.postDataJSON()
        messages.push(message(`u${messages.length}`, 'user', body.text))
        const bot = body.more ? message(`b${messages.length}`, 'bot', 'Here is the last full file.', files.slice(3))
          : message(`b${messages.length}`, 'bot', 'Here are the matching question crops.', [{ ...files[0], ref: 'r:question-1', kind: 'question', regionId: 'question-1', questionNumber: '1', box: { x: .1, y: .1, w: .8, h: .3 } }])
        messages.push(bot)
        return route.fulfill({ contentType: 'text/event-stream', body: `event: message\ndata: ${JSON.stringify({ conversation, message: bot })}\n\n` })
      }
      if (path.startsWith('/api/files/') && !path.endsWith('/view')) {
        const id = path.split('/')[3]
        const card = files.find(f => f.fileId === id)
        return json({ file: { id, title: card.fileTitle, ...card, parts: 2, status: 'ready' },
          pages: [1, 2].map(number => ({ id: `${id}-page-${number}`, number, width: 600, height: 800 })) })
      }
      if (path.endsWith('/view') || path === '/api/test-image') return route.fulfill({ contentType: 'image/svg+xml', body: picture })
      throw new Error(`Unexpected API request ${req.method()} ${path}`)
    })
    await page.goto(`${base}/chat/test-chat`)
    await expect(page.locator('.card')).toHaveCount(3)
    await expect(page.getByRole('link', { name: 'Open file', exact: true })).toHaveCount(3)
    await expect(page.getByText('Full file', { exact: true })).toHaveCount(3)
    expect(await page.getByRole('link', { name: /Download full file/ }).evaluateAll(links => links.map(a => a.getAttribute('href'))))
      .toEqual(files.slice(0, 3).map(f => `/api/files/${f.fileId}/download`))
    await page.getByRole('button', { name: /Show more/ }).click()
    await expect(page.locator('.card')).toHaveCount(4)
    await page.screenshot({ path: join(out, `${name}-full-files.png`), fullPage: true })
    await page.getByRole('link', { name: 'Open file', exact: true }).first().click()
    await expect(page).toHaveURL(/\/files\/file-1$/)
    await expect(page.locator('.doc-page')).toHaveCount(2)
    await expect(page.locator('.viewer')).toHaveCount(0)
    await page.goto(`${base}/chat/test-chat`)
    await page.getByLabel('Ask for material').fill('questions on queues in DS')
    await page.getByLabel('Ask for material').press('Enter')
    await expect(page.getByRole('button', { name: 'Open full page', exact: true })).toHaveCount(1)
    await page.getByRole('button', { name: 'Open full page', exact: true }).click()
    await expect(page.locator('.viewer')).toBeVisible()
    await page.keyboard.press('Escape')
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
    expect(errors).toEqual([])
    await context.close()
    console.log(`${name}: full files, show more, download links, whole original and topic-crop viewer passed`)
  }
  console.log(`Screenshots: ${out}`)
} finally { await browser.close() }
