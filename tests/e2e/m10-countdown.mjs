// M10 订单关单倒计时 · 真实浏览器走查（**只读**：不造单、不 reset、不发布消息）。
// 指向本地 vite dev（http://localhost:5173，/api 代理到真容器 5080），
// 把页面显示的倒计时与 GET /api/orders/{id} 的真实 expiresAtUtc 逐秒比对。
//
// 运行：NODE_PATH=web/node_modules M10_ORDER_ID=<Created订单id> node tests/e2e/m10-countdown.mjs
// 可选：M10_CLOSED_ID=<已关单订单id> 额外验证关单后不再显示倒计时。
// 依赖：六服务容器 docker compose up；web 目录 npx vite --host 127.0.0.1 --port 5173

import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require('playwright')

const BASE = process.env.M10_BASE || 'http://localhost:5173'
const API = process.env.M10_API || 'http://localhost:5080'
const ORDER_ID = process.env.M10_ORDER_ID
const CLOSED_ID = process.env.M10_CLOSED_ID
const results = []

function check(step, cond, detail = '') {
  results.push({ step, pass: !!cond, detail })
  console.log(`[${cond ? 'PASS' : 'FAIL'}] ${step}${detail ? ' — ' + detail : ''}`)
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

// 后端从 MySQL 读回的 DateTime 不带 Z，必须按 UTC 解释（与 web/src/utils/ttl.ts 同逻辑）。
function parseUtc(value) {
  const raw = String(value ?? '').trim()
  if (!raw) return NaN
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(raw)
  return Date.parse(hasZone ? raw : `${raw.replace(' ', 'T')}Z`)
}
function toSeconds(text) {
  const m = /^(\d{2}):(\d{2})$/.exec(String(text).trim())
  return m ? Number(m[1]) * 60 + Number(m[2]) : null
}

if (!ORDER_ID) {
  console.error('必须提供 M10_ORDER_ID（只读走查，脚本不会自己造单）')
  process.exit(2)
}

const browser = await chromium.launch({ headless: true })
const page = await browser.newPage({ viewport: { width: 1360, height: 900 } })
const consoleErrors = []
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()) })
page.on('pageerror', (e) => consoleErrors.push(String(e)))

try {
  const detail = await fetch(`${API}/api/orders/${ORDER_ID}`).then((r) => r.json())
  check('API 订单存在且为 Created（走查前提）', detail.status === 'Created', detail.status)
  check('API 返回真实 expiresAtUtc', typeof detail.expiresAtUtc === 'string' && detail.expiresAtUtc.length > 0,
    detail.expiresAtUtc)
  const deadline = parseUtc(detail.expiresAtUtc)
  check('expiresAtUtc 可按 UTC 解析', Number.isFinite(deadline), new Date(deadline).toISOString())

  await page.goto(`${BASE}/order/${ORDER_ID}`, { waitUntil: 'domcontentloaded' })
  await page.waitForSelector('.order-countdown__value', { timeout: 15000 })
  check('订单详情显示「距自动关单」',
    (await page.locator('.order-countdown__label').innerText()).trim() === '距自动关单')
  check('说明文案明确关单由 RabbitMQ 队列 TTL 自然到期触发',
    (await page.locator('.order-countdown').innerText()).includes('RabbitMQ 队列 TTL 自然到期触发'))

  const first = (await page.locator('.order-countdown__value').innerText()).trim()
  check('倒计时格式为 mm:ss', /^\d{2}:\d{2}$/.test(first), first)

  const expected = Math.floor((deadline - Date.now()) / 1000)
  const shown = toSeconds(first)
  check('页面倒计时与 API expiresAtUtc 对齐（误差 <= 2 秒，未把 UTC 当本地时间）',
    shown !== null && Math.abs(shown - expected) <= 2,
    `page=${first}(${shown}s) api=${expected}s`)

  await sleep(2200)
  const second = (await page.locator('.order-countdown__value').innerText()).trim()
  check('本地 1 秒时钟驱动倒计时真实递减',
    toSeconds(second) !== null && toSeconds(first) !== null && toSeconds(second) < toSeconds(first),
    `${first} -> ${second}`)

  check('未到期时不显示「已到期，等待关单」',
    !(await page.locator('.order-countdown').innerText()).includes('已到期'))

  if (CLOSED_ID) {
    await page.goto(`${BASE}/order/${CLOSED_ID}`, { waitUntil: 'domcontentloaded' })
    await page.waitForSelector('.shop-section__title', { timeout: 15000 })
    await sleep(600)
    check('已关单订单不再显示倒计时（不展示负时间）',
      await page.locator('.order-countdown__value').count() === 0)
    const cancelled = await fetch(`${API}/api/orders/${CLOSED_ID}`).then((r) => r.json())
    check('该订单确实已 Cancelled', cancelled.status === 'Cancelled', cancelled.status)
  }

  check('0 console / page error', consoleErrors.length === 0, consoleErrors.join(' | '))
} catch (e) {
  check('走查过程无异常', false, String(e))
} finally {
  await browser.close()
}

const pass = results.filter((r) => r.pass).length
const fail = results.length - pass
console.log('\n========== M10 倒计时浏览器走查汇总 ==========')
console.log(`PASS=${pass}  FAIL=${fail}`)
console.log(`E2E_EXIT=${fail ? 1 : 0}`)
process.exit(fail ? 1 : 0)
