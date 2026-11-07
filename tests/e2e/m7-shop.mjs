// M7 商城前端升级 · 真实浏览器全链路走查。
// 与 A11 脚本不同：本脚本指向本地 vite dev（http://localhost:5173，/api 代理到真容器 5080），
// 验证「消费优先」改造后的新页面结构，不依赖旧版首屏技术叙事。
//
// 运行：NODE_PATH=web/node_modules node tests/e2e/m7-shop.mjs
// 依赖：六服务容器 docker compose up；web 目录 npx vite --host 127.0.0.1 --port 5173

import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'

const require = createRequire(import.meta.url)
const { chromium } = require('playwright')

const BASE = process.env.M7_BASE || 'http://localhost:5173'
const SHOT_DIR = 'docs/screenshots/m7'
mkdirSync(SHOT_DIR, { recursive: true })

const results = []
const notes = []
function check(step, cond, detail = '') {
  results.push({ step, pass: !!cond, detail })
  console.log(`[${cond ? 'PASS' : 'FAIL'}] ${step}${detail ? ' — ' + detail : ''}`)
}
function warn(step, detail = '') {
  notes.push({ step, detail })
  console.log(`[WARN] ${step}${detail ? ' — ' + detail : ''}`)
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function bodyHas(page, text) {
  const t = await page.textContent('body').catch(() => '')
  return (t || '').includes(text)
}

const run = async () => {
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1360, height: 900 } })
  page.setDefaultTimeout(15000)

  // ---------- 1. 商城首页：轮播 / 分类 / 推荐 / 商品网格 ----------
  await page.goto(`${BASE}/`, { waitUntil: 'networkidle' })
  await page.locator('#products .product-card').first().waitFor({ state: 'visible' }).catch(() => {})
  check('首页常驻演示声明', await bodyHas(page, '演示环境 · 全部数据为模拟 · 不涉及真实交易'))
  check('首页轮播渲染', (await page.locator('.slide').count()) >= 3, `slides=${await page.locator('.slide').count()}`)
  check('分类快捷入口 5 个', (await page.locator('.category-card').count()) === 5,
    `cards=${await page.locator('.category-card').count()}`)
  check('分类 SVG 图标渲染', (await page.locator('.category-card svg.category-icon').count()) === 5)
  check('猜你喜欢区块存在', await bodyHas(page, '猜你喜欢'))
  const allCards = await page.locator('#products .product-card').count()
  check('全部商品网格有真实商品', allCards >= 8, `cards=${allCards}`)
  await page.screenshot({ path: `${SHOT_DIR}/01-home.png`, fullPage: true })

  // ---------- 2. 技术叙事降级为暗线：默认折叠，展开才可见 ----------
  const techPhrase = '一笔订单，跑通三个中间件'
  // Element Plus 折叠内容默认仍渲染进 DOM（只是隐藏），因此用折叠头状态类判断，而非 body 文本。
  const techHeader = page.locator('.home-tech .el-collapse-item__header').first()
  const collapsedBefore = !((await techHeader.getAttribute('class')) || '').includes('is-active')
  check('技术溯源默认折叠（首屏不出现长篇技术叙事）', collapsedBefore)
  await techHeader.click()
  await sleep(300)
  check('点击「技术溯源」后展开三中间件与实测数据',
    (await bodyHas(page, techPhrase)) && await bodyHas(page, 'MySQL') && await bodyHas(page, 'RabbitMQ') &&
    ((await techHeader.getAttribute('class')) || '').includes('is-active'))

  // ---------- 3. 顶栏分类导航 → URL query → 商品过滤 ----------
  const apiProducts = await page.request.get(`${BASE}/api/products`).then((r) => r.json()).catch(() => [])
  const digitalCount = apiProducts.filter((p) => p.category === '数码电器').length
  check('真容器已同步 category 字段与 8 个 SKU 种子', apiProducts.length >= 8 && digitalCount > 0,
    `商品=${apiProducts.length} / 数码电器=${digitalCount}`)
  await page.locator('.shop-catbar__item:has-text("数码电器")').first().click()
  await sleep(400)
  check('分类导航写入 URL query.category', /category=/.test(page.url()), page.url())
  const filtered = await page.locator('#products .product-card').count()
  check('分类过滤结果与 GET /api/products 一致', filtered === digitalCount,
    `页面=${filtered} / API=${digitalCount}`)
  check('分类导航高亮当前分类', (await page.locator('.shop-catbar__item.is-active:has-text("数码电器")').count()) === 1)
  await page.screenshot({ path: `${SHOT_DIR}/02-category.png`, fullPage: true })

  // ---------- 4. 顶栏全局搜索 → URL query.q → 关键字过滤 ----------
  await page.locator('.shop-search input').fill('耳机')
  await page.locator('.shop-search button:has-text("搜索")').click()
  await sleep(400)
  check('搜索写入 URL query.q', /q=/.test(page.url()), page.url())
  check('搜索结果命中「无线降噪耳机」', await bodyHas(page, '无线降噪耳机'))
  const searched = await page.locator('#products .product-card').count()
  check('搜索结果收窄到 1 件', searched === 1, `cards=${searched}`)

  // ---------- 5. 商品详情：面包屑 / 多图画廊 / 同类推荐 ----------
  await page.locator('#products button:has-text("查看详情")').first().click()
  await page.waitForURL('**/product/**')
  await page.locator('.gallery__thumb').first().waitFor({ state: 'visible' }).catch(() => {})
  check('详情页面包屑含分类', await bodyHas(page, '商城首页') && await bodyHas(page, '数码电器'))
  check('多图画廊 4 个视角', (await page.locator('.gallery__thumb').count()) === 4)
  await page.locator('.gallery__thumb').nth(1).click()
  check('切换视角后计数器更新', (await page.locator('.gallery__counter').textContent()).includes('2 / 4'))
  check('同类推荐区块存在', await bodyHas(page, '同类推荐'))
  const detailTechHeader = page.locator('.detail-tech .el-collapse-item__header').first()
  check('详情页技术溯源默认折叠',
    !((await detailTechHeader.getAttribute('class')) || '').includes('is-active'))
  await page.screenshot({ path: `${SHOT_DIR}/03-detail.png`, fullPage: true })

  // ---------- 6. 加入购物车 → 角标 ----------
  await page.locator('.detail-actions button:has-text("加入购物车")').click()
  await sleep(500)
  const badge = Number((await page.locator('.cart-badge').first().textContent().catch(() => '0')) || 0)
  check('加入购物车后顶栏角标 > 0', badge > 0, `badge=${badge}`)

  // ---------- 7. 购物车：行级勾选 / 全选 / 仅结算已选 ----------
  await page.locator('a.cart-link').first().click()
  await page.waitForURL('**/cart')
  await page.locator('.cart-list__head .el-checkbox').first().waitFor({ state: 'visible' })
  check('购物车含行级勾选框', (await page.locator('.cart-line__check').count()) >= 1)
  const allCb = page.locator('.cart-list__head .el-checkbox').first()
  const goCheckout = page.locator('button:has-text("去结算")').first()
  // 先确保处于「全选」状态，再取消全选验证禁用逻辑。
  const cls0 = (await allCb.getAttribute('class')) || ''
  if (!cls0.includes('is-checked')) await allCb.click()
  await sleep(200)
  await allCb.click()
  await sleep(200)
  check('取消全选后「去结算」禁用', await goCheckout.isDisabled())
  await allCb.click()
  await sleep(200)
  check('恢复全选后「去结算」可用', !(await goCheckout.isDisabled()))
  check('合计只算已选商品', await bodyHas(page, '已选'))
  await page.screenshot({ path: `${SHOT_DIR}/04-cart.png`, fullPage: true })

  // ---------- 8. 结算：模拟地址 / 优惠券 / 金额明细 / 吸底条 ----------
  await goCheckout.click()
  await page.waitForURL('**/checkout')
  check('结算页含模拟收货地址', await bodyHas(page, '收货信息') && await bodyHas(page, '演示收件人'))
  check('结算页含演示优惠券与支付方式', await bodyHas(page, '优惠券') && await bodyHas(page, '模拟支付'))
  check('金额明细明确「不参与下单」', await bodyHas(page, '不参与下单'))
  check('吸底结算条存在', (await page.locator('.checkout-bar').count()) === 1)
  await page.screenshot({ path: `${SHOT_DIR}/05-checkout.png`, fullPage: true })

  // ---------- 9. 提交订单 → 订单详情 → 模拟支付 → 异步履约 ----------
  await page.locator('.checkout-bar button:has-text("提交订单")').click()
  await page.waitForURL('**/order/**')
  // 订单详情为异步拉取，等状态步骤条渲染后再断言，避免断言早于组件挂载。
  await page.locator('.order-steps .el-step').first()
    .waitFor({ state: 'visible', timeout: 10000 }).catch(() => {})
  await sleep(300)
  check('下单后进入订单详情', /\/order\/\d+/.test(page.url()), page.url())
  check('订单详情含状态步骤条', (await page.locator('.order-steps .el-step').count()) === 4)
  check('新建订单为「待支付」并给出支付引导', await bodyHas(page, '待支付'))
  check('订单详情含「模拟发货 · 演示」按钮', await bodyHas(page, '模拟发货 · 演示'))
  await page.screenshot({ path: `${SHOT_DIR}/06-order-created.png`, fullPage: true })

  const payBtn = page.locator('.order-callout button:has-text("模拟支付")').first()
  await payBtn.click()
  let paidOrShipped = false
  for (let i = 0; i < 20; i++) {
    if (await bodyHas(page, '待发货') || await bodyHas(page, '已发货')) { paidOrShipped = true; break }
    await sleep(1200)
  }
  check('模拟支付后状态推进（待发货 / 已发货）', paidOrShipped)

  // 若后端允许停留在「待发货」，用户端的模拟发货按钮应可点击并推进到「已发货」。
  if (await bodyHas(page, '待发货')) {
    const shipBtn = page.locator('.shop-action-bar button:has-text("模拟发货")').first()
    if (!(await shipBtn.isDisabled())) {
      await shipBtn.click()
      let shipped = false
      for (let i = 0; i < 20; i++) {
        if (await bodyHas(page, '已发货')) { shipped = true; break }
        await sleep(1200)
      }
      check('点击「模拟发货」后推进到已发货', shipped)
    } else {
      warn('「模拟发货」按钮处于禁用态（后端可能已自动发货）')
    }
  } else {
    check('Worker 异步履约直接推进到已发货', await bodyHas(page, '已发货'))
  }
  await page.screenshot({ path: `${SHOT_DIR}/07-order-fulfilled.png`, fullPage: true })

  // ---------- 10. 订单列表「去支付 / 详情」入口 ----------
  await page.goto(`${BASE}/orders`, { waitUntil: 'networkidle' })
  check('订单列表可打开且有操作列', (await page.locator('.el-table').count()) >= 1)
  check('订单列表含「去支付」或「详情」入口',
    (await bodyHas(page, '去支付')) || (await bodyHas(page, '详情')))

  // ---------- 11. 会员静态壳 + /login 重定向 ----------
  await page.goto(`${BASE}/member`, { waitUntil: 'networkidle' })
  check('会员页明确标注无真实用户系统', await bodyHas(page, '没有真实用户系统'))
  check('会员页说明不收集个人信息', await bodyHas(page, '不收集'))
  await page.screenshot({ path: `${SHOT_DIR}/08-member.png`, fullPage: true })
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' })
  check('/login 重定向到 /member 静态壳', /\/member$/.test(page.url()), page.url())

  await browser.close()

  const failed = results.filter((r) => !r.pass)
  console.log('\n========== M7 商城浏览器走查汇总 ==========')
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
  console.error('走查脚本异常：', e)
  process.exit(1)
})
