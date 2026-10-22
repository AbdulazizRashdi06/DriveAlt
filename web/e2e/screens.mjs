// Drives the running app (API on :5257 serving the built SPA) and screenshots every screen at desktop and phone size.
// Usage: node e2e/screens.mjs [outDir]
import { chromium } from '@playwright/test'
import { mkdirSync } from 'node:fs'

const BASE = process.env.BASE_URL ?? 'http://localhost:5257'
const OUT = process.argv[2] ?? 'e2e/shots'
const UNI = process.env.UNI_ID ?? '24-0161'
mkdirSync(OUT, { recursive: true })

const browser = await chromium.launch({ executablePath: process.env.CHROME ?? '/opt/pw-browsers/chromium' })
const errors = []

async function login(page) {
  await page.goto(`${BASE}/login`)
  await page.getByLabel('Uni ID').fill(UNI.replace('-', ''))
  // The server allows one code per 45 s per ID; keep asking until it sends one.
  for (let i = 0; i < 8; i++) {
    await page.getByRole('button', { name: 'Email me a code' }).click()
    if (await page.getByText('Type the code').waitFor({ timeout: 2500 }).then(() => true, () => false)) break
    await page.waitForTimeout(8000)
  }
  const { code } = await (await page.request.get(`${BASE}/api/dev/otp/${UNI}`)).json()
  await page.keyboard.type(code)
  await page.waitForURL(/\/chat/)
}

let session = null

for (const [name, viewport, mobile] of [['desktop', { width: 1440, height: 1000 }, false], ['phone', { width: 390, height: 844 }, true]]) {
  const opts = { viewport, isMobile: mobile, hasTouch: mobile, deviceScaleFactor: mobile ? 2 : 1 }
  const anon = await browser.newContext(opts)
  const loginPage = await anon.newPage()
  await loginPage.goto(`${BASE}/login`)
  await loginPage.waitForTimeout(600)
  await loginPage.screenshot({ path: `${OUT}/${name}-01-login.png` })
  await anon.close()

  const ctx = await browser.newContext(session ? { ...opts, storageState: session } : opts)
  const page = await ctx.newPage()
  page.on('pageerror', e => errors.push(`${name}: ${e.message}`))
  // 429s are the OTP cooldown doing its job while the script waits for a code.
  page.on('console', m => { if (m.type() === 'error' && !m.text().includes('429')) errors.push(`${name} console: ${m.text()}`) })
  const shot = async (n, full = !mobile) => { await page.waitForTimeout(500); await page.screenshot({ path: `${OUT}/${name}-${n}.png`, fullPage: full }) }

  if (!session) { await login(page); session = await ctx.storageState() }
  await page.goto(`${BASE}/chat`)
  await shot('02-chat-empty')

  await page.getByLabel('Ask for material').fill('past paper questions on circular queue rear in DS')
  await page.getByLabel('Ask for material').press('Enter')
  await page.locator('.card').first().waitFor({ timeout: 30000 })
  await page.getByLabel('Ask for material').fill("what's the answer to Q1?")
  await page.getByLabel('Ask for material').press('Enter')
  await page.getByText(/Abdulaziz/).last().waitFor({ timeout: 30000 })
  await page.locator('.card').first().getByRole('button', { name: /^Save/ }).click()
  await shot('03-chat')

  await page.locator('.card .shot').first().click()
  await page.locator('.viewer').waitFor()
  await shot('04-viewer', false)
  await page.keyboard.press('Escape')

  await page.goto(`${BASE}/browse`)
  await shot('05-browse-root')
  await page.goto(`${BASE}/browse/data-structures`)
  await page.locator('.file').first().waitFor()
  await page.locator('.file').first().click()
  await page.locator('.pg').first().waitFor()
  await shot('06-browse-file')

  await page.goto(`${BASE}/saved`)
  await shot('07-saved')
  await page.goto(`${BASE}/upload`)
  await page.locator('.row').first().waitFor()
  await shot('08-upload')
  await page.goto(`${BASE}/inspect`)
  await page.locator('.file').first().waitFor()
  await page.locator('.file').first().click()
  await page.locator('.page-fig img').waitFor()
  await shot('09-inspect')

  const fileHref = await page.getByRole('link', { name: 'Open original' }).getAttribute('href')
  await page.goto(`${BASE}${fileHref}`)
  await page.locator('.doc-page').first().waitFor()
  await shot('10-original')
  await page.goto(`${BASE}/admin`)
  await shot('11-admin')
  await ctx.close()
}

await browser.close()
if (errors.length) { console.log('ERRORS:\n' + errors.join('\n')); process.exitCode = 1 }
else console.log('ok: screenshots in ' + OUT)
