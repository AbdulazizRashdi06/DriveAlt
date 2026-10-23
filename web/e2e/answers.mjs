// Checks the answer-screenshot flow: a question card flips to its answer, and asking the bot for the answer sends the screenshot.
// Usage: node e2e/answers.mjs [outDir]   (needs a page with question + answer regions, e.g. labelled in Inspect)
import { chromium } from '@playwright/test'
const BASE = process.env.BASE_URL ?? 'http://localhost:5257'
const OUT = process.argv[2] ?? 'e2e/shots'
const UNI = process.env.UNI_ID ?? '24-0161'
const browser = await chromium.launch({ executablePath: process.env.CHROME ?? '/opt/pw-browsers/chromium' })
const page = await (await browser.newContext({ viewport: { width: 1440, height: 1000 } })).newPage()
await page.goto(`${BASE}/login`)
await page.getByLabel('Uni ID').fill(UNI.replace('-', ''))
await page.getByRole('button', { name: 'Email me a code' }).click()
await page.getByText('Type the code').waitFor()
const { code } = await (await page.request.get(`${BASE}/api/dev/otp/${UNI}`)).json()
await page.keyboard.type(code)
await page.waitForURL(/\/chat/)

const ask = async text => {
  const before = await page.locator('.bubble-bot').count()
  await page.getByLabel('Ask for material').fill(text)
  await page.getByLabel('Ask for material').press('Enter')
  await page.waitForFunction(n => document.querySelectorAll('.bubble-bot').length > n && !document.querySelector('.typing'), before)
}
await ask('past paper questions on circular queue in DS')
const toggle = page.getByRole('button', { name: 'Show answer' }).first()
await toggle.click()
await page.waitForTimeout(500)
await page.screenshot({ path: `${OUT}/answer-toggle.png` })

await ask("ok what's the answer to Q1?")
const last = page.locator('.bot-row').last()
const text = await last.locator('.bubble-bot').innerText()
const label = await last.locator('.card-top span').last().innerText()
console.log('bot:', text, '| card:', label)
if (!/Abdulaziz/.test(text) || !/ANSWER/i.test(label)) { console.error('expected an Abdulaziz line with an answer screenshot'); process.exitCode = 1 }
await page.waitForTimeout(500)
await last.screenshot({ path: `${OUT}/answer-asked.png` })
await browser.close()
