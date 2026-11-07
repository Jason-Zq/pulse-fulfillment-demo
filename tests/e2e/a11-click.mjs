// A11 浏览器逐页点击实测（M6d / 6.16）。
// 真容器指向 http://localhost:8080，用真实 chromium 点击关键用户旅程，并验证
// 「下单 → 模拟支付 → Worker 异步履约 → 已发货」全链路在真实浏览器里跑通。
// 不引入任何业务依赖；仅用 playwright（装在 web/node_modules，NODE_PATH 注入）。
//
// 运行：node tests/e2e/a11-click.mjs
// 依赖：六服务容器已 docker compose up；web/node_modules 已含 playwright + chromium。

import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'

// ESM 不读 NODE_PATH；用 createRequire（CJS 解析会读 NODE_PATH）加载 playwright，
// 这样脚本可放在仓库任意位置，运行时只需 NODE_PATH=web/node_modules。
const require = createRequire(import.meta.url)
const { chromium } = require('playwright')

const BASE = process.env.A11_BASE || 'http://localhost:8080'
const SHOT_DIR = 'docs/screenshots'
mkdirSync(SHOT_DIR, { recursive: true })

const results = []
const notes = []
function check(step, cond, detail = '') {
  results.push({ step, pass: !!cond, detail })
  const tag = cond ? 'PASS' : 'FAIL'
  console.log(`[${tag}] ${step}${detail ? ' — ' + detail : ''}`)
}
function warn(step, detail = '') {
  notes.push({ step, detail })
  console.log(`[WARN] ${step}${detail ? ' — ' + detail : ''}`)
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

// 解析 ElTag 中文标签是否出现在页面文本里
async function bodyHas(page, text) {
  const t = await page.textContent('body').catch(() => '')
  return (t || '').includes(text)
}

const run = async () => {
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
  page.setDefaultTimeout(15000)

  // 起始：重置干净演示数据（保留 experiment_results 留痕）
  try {
    const r = await page.request.post(`${BASE}/api/lab/reset`)
    console.log('reset status =', r.status())
  } catch (e) {
    warn('lab/reset 调用失败，继续（可能是演示数据已干净）', e.message)
  }

  // ---------- 1. 商城首页 ----------
  await page.goto(`${BASE}/`, { waitUntil: 'networkidle' })
  const homeTitle = await page.title()
  check('首页标题含项目名', /Pulse/.test(homeTitle), homeTitle)
  check('首页常驻演示声明', await bodyHas(page, '演示环境 · 全部数据为模拟 · 不涉及真实交易'))
  check('首页展示三中间件标签', await bodyHas(page, 'MySQL') && await bodyHas(page, 'Redis') && await bodyHas(page, 'RabbitMQ'))
  await page.screenshot({ path: `${SHOT_DIR}/01-home.png`, fullPage: true })

  // ---------- 2. 商品详情（点击第一张卡片的「查看详情」，应用内路由保留 cart store）----------
  const detailBtn = page.locator('button:has-text("查看详情")').first()
  await detailBtn.click()
  await page.waitForURL('**/product/**')
  check('进入商品详情页（URL 含 /product/）', /\/product\//.test(page.url()), page.url())
  // 商品详情为异步拉取数据，先等「加入购物车」按钮可见再断言（避免断言早于组件渲染）。
  const addBtn = page.locator('button:has-text("加入购物车")').first()
  await addBtn.waitFor({ state: 'visible', timeout: 10000 }).catch(() => {})
  check('商品详情含「加入购物车」', (await addBtn.count()) > 0)
  await page.screenshot({ path: `${SHOT_DIR}/02-product.png`, fullPage: true })

  // ---------- 3. 加入购物车 ----------
  await page.locator('button:has-text("加入购物车")').click()
  await sleep(600) // 等 ElMessage + cart store 更新
  // 通过顶部购物车角标确认数量 > 0
  const badge = await page.locator('.cart-badge').first().textContent().catch(() => '0')
  check('加入购物车后角标计数 > 0', Number(badge) > 0, `badge=${badge}`)

  // ---------- 4. 购物车（点顶部购物车链接，应用内路由）----------
  await page.locator('a.cart-link').first().click()
  await page.waitForURL('**/cart')
  check('进入购物车页', /\/cart$/.test(page.url()), page.url())
  check('购物车含「去结算」', await bodyHas(page, '去结算'))
  await page.screenshot({ path: `${SHOT_DIR}/03-cart.png`, fullPage: true })

  // ---------- 5. 结算 ----------
  await page.locator('button:has-text("去结算")').click()
  await page.waitForURL('**/checkout')
  check('进入结算页', /\/checkout$/.test(page.url()), page.url())
  check('结算页含「提交订单」', await bodyHas(page, '提交订单'))
  await page.screenshot({ path: `${SHOT_DIR}/04-checkout.png`, fullPage: true })

  // ---------- 6. 提交订单（单 SKU 路径）----------
  await page.locator('button:has-text("提交订单")').click()
  await page.waitForURL('**/order/**')
  check('下单后进入订单详情页（URL 含 /order/）', /\/order\//.test(page.url()), page.url())
  const orderNo = (await page.locator('.shop-mono').first().textContent().catch(() => '')) || ''
  check('订单详情展示 orderNo', orderNo.trim().length > 0, `orderNo=${orderNo.trim()}`)
  check('新建订单初始状态为「待支付」', await bodyHas(page, '待支付'))
  await page.screenshot({ path: `${SHOT_DIR}/05-order-created.png`, fullPage: true })

  // ---------- 7. 模拟支付 → 轮询到「已发货」（验证 RabbitMQ→Worker→MySQL 真实闭环）----------
  await page.locator('.shop-action-bar button:has-text("模拟支付")').click()
  await sleep(1000)
  let shipped = false
  for (let i = 0; i < 25; i++) {
    if (await bodyHas(page, '已发货')) { shipped = true; break }
    await sleep(1500)
  }
  check('模拟支付后 Worker 异步履约 → 状态变为「已发货」', shipped, shipped ? '全链路闭环成功' : '25 次轮询内未等到已发货')
  await page.screenshot({ path: `${SHOT_DIR}/06-order-shipped.png`, fullPage: true })

  // ---------- 8. 后台仪表盘：7 节点链路泳道 + E1/E2/E3 ----------
  await page.goto(`${BASE}/admin/dashboard`, { waitUntil: 'networkidle' })
  await page.waitForSelector('text=下单事务')
  const swimlaneLabels = ['下单事务', 'Outbox 待投递', '消息投递', '消费判重', '库存实扣', '死信待补偿', '日报落表']
  const missing = []
  for (const lbl of swimlaneLabels) {
    if (!(await bodyHas(page, lbl))) missing.push(lbl)
  }
  check('仪表盘 7 节点链路泳道齐全', missing.length === 0, missing.length ? `缺失: ${missing.join(',')}` : '7/7')
  // 直接用 API 取真实计数作为证据
  const pipe = await page.request.get(`${BASE}/api/pipeline`).then((r) => r.json()).catch(() => null)
  if (pipe && Array.isArray(pipe.nodes)) {
    const byKey = Object.fromEntries(pipe.nodes.map((n) => [n.key, n]))
    const orders = byKey.orders?.count
    const deduct = byKey.deduct?.count
    const processed = byKey.worker?.count
    check('GET /api/pipeline 计数为整数', swimlaneLabels.every((_, i) => Number.isInteger(pipe.nodes[i]?.count)),
      JSON.stringify(pipe.nodes.map((n) => `${n.key}=${n.count}`)))
    check('下单后 orders 计数 >= 1', Number(orders) >= 1, `orders=${orders}`)
    check('履约后 库存实扣(deduct) >= 1', Number(deduct) >= 1, `deduct=${deduct}`)
    check('Worker 消费判重计数 >= 1（证明消息被消费）', Number(processed) >= 1, `processed=${processed}`)
  } else {
    warn('无法读取 /api/pipeline', '')
  }
  // E1/E2/E3 实测结论卡（依赖实验台历史结果；缺失只告警不判失败）
  const hasE = await bodyHas(page, 'E1') && await bodyHas(page, 'E2') && await bodyHas(page, 'E3')
  if (hasE) check('仪表盘展示 E1/E2/E3 实测结论卡', true)
  else warn('仪表盘未显示 E1/E2/E3 卡（依赖实验台历史结果，可先跑实验台）')
  await page.screenshot({ path: `${SHOT_DIR}/07-dashboard.png`, fullPage: true })

  // ---------- 9. 8 个后台页：各能打开且统一含「本页技术证据」卡 ----------
  const adminPages = [
    ['dashboard', '仪表盘'], ['alerts', '告警中心'], ['lab', '实验台'], ['orders', '订单列表'],
    ['products', '商品与库存'], ['fulfillment', '发货与售后'], ['reports', '对账报表'], ['dead-letters', '死信补偿台'],
  ]
  let adminOk = 0
  for (const [path, title] of adminPages) {
    await page.goto(`${BASE}/admin/${path}`, { waitUntil: 'networkidle' })
    const ok = await bodyHas(page, '本页技术证据')
    if (ok) adminOk++
    else warn(`后台页 /admin/${path} 未渲染「本页技术证据」卡`)
    await page.screenshot({ path: `${SHOT_DIR}/admin-${path}.png`, fullPage: true })
  }
  check('8 个后台页均含统一技术证据卡', adminOk === 8, `${adminOk}/8`)

  // ---------- 收尾：恢复干净演示数据 ----------
  try {
    const r = await page.request.post(`${BASE}/api/lab/reset`)
    console.log('reset(收尾) status =', r.status())
  } catch (e) {
    warn('收尾 reset 失败', e.message)
  }

  await browser.close()

  const failed = results.filter((r) => !r.pass)
  console.log('\n========== A11 浏览器实测汇总 ==========')
  console.log(`PASS=${results.filter((r) => r.pass).length}  FAIL=${failed.length}  WARN=${notes.length}`)
  if (failed.length) {
    console.log('失败项：')
    failed.forEach((r) => console.log(`  - ${r.step} :: ${r.detail}`))
  }
  if (notes.length) {
    console.log('告警项（不计入失败）：')
    notes.forEach((n) => console.log(`  - ${n.step} :: ${n.detail}`))
  }
  process.exit(failed.length ? 1 : 0)
}

run().catch((e) => {
  console.error('A11 运行异常：', e)
  process.exit(2)
})
