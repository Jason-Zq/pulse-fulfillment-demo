// M9 作品集演示导览 · 真实浏览器走查。
// 指向本地 vite dev（http://localhost:5173，/api 代理到真容器 5080），
// 验证 /tour 的入口、7 节点计数确实来自 GET /api/pipeline、复现命令与追问 Q&A。
//
// 运行：NODE_PATH=web/node_modules node tests/e2e/m9-tour.mjs
// 依赖：六服务容器 docker compose up；web 目录 npx vite --host 127.0.0.1 --port 5173

import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require('playwright')

const BASE = process.env.M9_BASE || 'http://localhost:5173'
const API = process.env.M9_API || 'http://localhost:5080'
const ORDER = ['orders', 'outbox', 'exchange', 'worker', 'deduct', 'deadLetter', 'report']
const results = []

function check(step, cond, detail = '') {
  results.push({ step, pass: !!cond, detail })
  console.log(`[${cond ? 'PASS' : 'FAIL'}] ${step}${detail ? ' — ' + detail : ''}`)
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

const browser = await chromium.launch({ headless: true })
const page = await browser.newPage({ viewport: { width: 1360, height: 900 } })
const consoleErrors = []
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()) })
page.on('pageerror', (e) => consoleErrors.push(String(e)))

try {
  await page.goto(BASE + '/', { waitUntil: 'domcontentloaded' })
  await page.waitForSelector('.shop-topnav__tech', { timeout: 15000 })
  check('顶栏存在「技术演示」入口',
    (await page.locator('.shop-topnav__tech').innerText()).trim() === '技术演示')
  check('常驻演示声明',
    (await page.locator('.el-alert').first().innerText()).includes('不涉及真实交易'))

  await page.locator('.home-tech .el-collapse-item__header').click()
  await sleep(200)
  check('首页技术溯源可展开',
    await page.locator('.home-tech .el-collapse-item__header').first()
      .evaluate((el) => el.classList.contains('is-active')))
  check('折叠块含演示导览链接', await page.locator('.home-tech a[href="/tour"]').count() >= 1)

  await page.locator('.shop-topnav__tech').click()
  await page.waitForURL('**/tour')
  await page.waitForSelector('.tour-steps .tour-step', { timeout: 15000 })
  check('进入 /tour 演示导览', page.url().endsWith('/tour'))
  check('导览标题点明三个中间件',
    (await page.locator('h1').first().innerText()).includes('三个中间件'))

  const stepCount = await page.locator('.tour-step').count()
  check('7 步链路卡齐全', stepCount === 7, `count=${stepCount}`)

  // 数字诚实：逐一比对页面计数与 GET /api/pipeline 的真实值（不可用一律「—」）。
  const snapshot = await fetch(API + '/api/pipeline').then((r) => r.json())
  const apiCounts = Object.fromEntries(snapshot.nodes.map((n) => [
    n.key, (n.available === false || n.count < 0) ? '—' : String(n.count),
  ]))
  const mismatches = []
  for (let i = 0; i < ORDER.length; i += 1) {
    const shown = (await page.locator('.tour-step').nth(i)
      .locator('.tour-step__count').innerText()).trim().split(/\s+/)[0]
    if (shown !== apiCounts[ORDER[i]]) {
      mismatches.push(`${ORDER[i]}: page=${shown} api=${apiCounts[ORDER[i]]}`)
    }
  }
  check('7 节点计数与 /api/pipeline 逐一相等（不写死数字）',
    mismatches.length === 0, mismatches.join('; '))
  check('页面显示生成时间而非示例时间',
    (await page.locator('.shop-section__hint').first().innerText()).includes(snapshot.generatedAtUtc.slice(0, 10)))

  const codes = await page.locator('.tour-cmd__code').allInnerTexts()
  check('复现命令含下单 /api/orders', codes.some((t) => t.includes('/api/orders')))
  check('复现命令含 /api/pipeline', codes.some((t) => t.includes('/api/pipeline')))
  check('每条命令都有复制按钮', await page.locator('.tour-cmd .el-button').count() >= codes.length)

  const evidence = await page.locator('.shop-evidence').innerText()
  check('实验证据区展示真实结果或明确空态',
    /暂无实测数据|超卖|实扣|已补投/.test(evidence))
  const qaHeaders = page.locator('.tour-qa .el-collapse-item__header')
  check('追问 Q&A 共 8 条', await qaHeaders.count() === 8, `count=${await qaHeaders.count()}`)
  await qaHeaders.first().click()
  await sleep(200)
  check('Q&A 可展开',
    await qaHeaders.first().evaluate((el) => el.classList.contains('is-active')))

  const links = await page.locator('.tour-step__links a')
    .evaluateAll((els) => els.map((e) => e.getAttribute('href')))
  check('7 步每步都有后台核对链接',
    links.length === 7 && links.every((h) => h && h.startsWith('/admin/')),
    links.join(','))
  check('无浏览器 console / page error', consoleErrors.length === 0,
    consoleErrors.slice(0, 2).join(' | '))
} catch (e) {
  check('走查过程无异常', false, String(e))
} finally {
  await browser.close()
}

const passed = results.filter((r) => r.pass).length
const failed = results.length - passed
console.log(`\n========== M9 演示导览浏览器走查汇总 ==========\nPASS=${passed}  FAIL=${failed}`)
process.exit(failed === 0 ? 0 : 1)
