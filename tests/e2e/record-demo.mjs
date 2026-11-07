// 6.17 演示视频录制（辅助项）：用真实 chromium 录下核心用户旅程，
// 输出 docs/screenshots/demo.webm，再由 playwright 自带 ffmpeg 转成 demo.gif。
// 复用 A11 的旅程，但开启 recordVideo。
//
// 运行：node tests/e2e/record-demo.mjs
// 依赖：六服务容器已启动；web/node_modules 含 playwright + chromium。

import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'

const require = createRequire(import.meta.url)
const { chromium } = require('playwright')

const BASE = process.env.A11_BASE || 'http://localhost:8080'
const SHOT_DIR = 'docs/screenshots'
mkdirSync(SHOT_DIR, { recursive: true })
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

const run = async () => {
  const browser = await chromium.launch()
  const context = await browser.newContext({
    viewport: { width: 1280, height: 900 },
    recordVideo: { dir: SHOT_DIR, size: { width: 1280, height: 900 } },
  })
  const page = await context.newPage()
  page.setDefaultTimeout(15000)

  try { await page.request.post(`${BASE}/api/lab/reset`) } catch {}
  await page.goto(`${BASE}/`, { waitUntil: 'networkidle' })
  await sleep(1200)

  const detailBtn = page.locator('button:has-text("查看详情")').first()
  await detailBtn.click()
  await page.waitForURL('**/product/**')
  await sleep(800)
  const addBtn = page.locator('button:has-text("加入购物车")').first()
  await addBtn.waitFor({ state: 'visible', timeout: 10000 }).catch(() => {})
  await addBtn.click()
  await sleep(800)

  await page.locator('a.cart-link').first().click()
  await page.waitForURL('**/cart')
  await sleep(800)
  await page.locator('button:has-text("去结算")').click()
  await page.waitForURL('**/checkout')
  await sleep(800)
  await page.locator('button:has-text("提交订单")').click()
  await page.waitForURL('**/order/**')
  await sleep(1000)

  await page.locator('.shop-action-bar button:has-text("模拟支付")').click()
  await sleep(1000)
  let shipped = false
  for (let i = 0; i < 25; i++) {
    const t = await page.textContent('body').catch(() => '')
    if ((t || '').includes('已发货')) { shipped = true; break }
    await sleep(1500)
  }
  await sleep(800)

  await page.goto(`${BASE}/admin/dashboard`, { waitUntil: 'networkidle' })
  await page.waitForSelector('text=下单事务')
  await sleep(1500)

  try { await page.request.post(`${BASE}/api/lab/reset`) } catch {}

  // 正确顺序：先取 video 句柄 → 关闭 context（刷新录制文件）→ saveAs → 最后关 browser。
  const video = page.video()
  const out = `${SHOT_DIR}/demo.webm`
  await context.close()
  if (video) await video.saveAs(out)
  await browser.close()
  console.log('shipped =', shipped)
  console.log('video saved =', out)
  process.exit(shipped ? 0 : 1)
}

run().catch((e) => { console.error('record 异常：', e); process.exit(2) })
