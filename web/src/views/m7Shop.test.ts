import { describe, it, expect } from 'vitest'
import productsSource from './Products.vue?raw'
import cartSource from './Cart.vue?raw'
import checkoutSource from './Checkout.vue?raw'
import productDetailSource from './ProductDetail.vue?raw'
import orderDetailSource from './OrderDetail.vue?raw'
import ordersSource from './Orders.vue?raw'
import memberSource from './Member.vue?raw'
import shopLayoutSource from '../layouts/ShopLayout.vue?raw'
import gallerySource from '../components/ProductGallery.vue?raw'
import categoryIconSource from '../components/CategoryIcon.vue?raw'
import { PRODUCT_CATEGORIES, ALL_CATEGORY } from '../utils/categories'
import { routes } from '../router/routes'

function mustContain(src: string, tokens: string[]): void {
  expect(src.length).toBeGreaterThan(0)
  for (const token of tokens) expect(src).toContain(token)
}

describe('M7 分类常量与路由', () => {
  it('五个分类与后端 Program.cs 种子一致，全部占位值为 all', () => {
    expect(PRODUCT_CATEGORIES).toEqual([
      '数码电器', '家居生活', '服饰鞋包', '美妆个护', '食品生鲜',
    ])
    expect(ALL_CATEGORY).toBe('all')
  })

  it('/member 为演示静态壳路由，/login 重定向到 /member', () => {
    const raw = routes as unknown as Array<{ path: string; component?: unknown; redirect?: unknown }>
    const member = raw.find((r) => r.path === '/member')
    const login = raw.find((r) => r.path === '/login')
    expect(typeof member?.component).toBe('function')
    expect(login?.redirect).toBe('/member')
    expect(login?.component).toBeUndefined()
  })
})

describe('M7 顶栏与首页（消费优先、技术为暗线）', () => {
  it('ShopLayout：全局搜索 + 分类导航 + 购物车角标 + 会员入口', () => {
    mustContain(shopLayoutSource, [
      'submitSearch', 'PRODUCT_CATEGORIES', 'shop-catbar',
      'cart.totalCount', '/member', 'DEMO_BANNER',
    ])
  })

  it('首页：轮播 + 分类图标 + 猜你喜欢 + 商品网格，技术叙事折叠', () => {
    mustContain(productsSource, [
      'ElCarousel', 'CategoryIcon', '猜你喜欢', '全部商品',
      'product-grid', '技术溯源', 'ElCollapse', 'PRODUCT_CATEGORIES',
      'parseLatestEvidence', 'listExperiments',
    ])
    expect(productsSource).not.toContain('75.97')
  })

  it('分类图标组件覆盖五个分类且零外部资源', () => {
    for (const c of PRODUCT_CATEGORIES) expect(categoryIconSource).toContain(c)
    expect(categoryIconSource).toContain('<svg')
    expect(categoryIconSource).not.toContain('http')
  })

  it('多图画廊：四个视角 + variant + 切换 SKU 复位', () => {
    mustContain(gallerySource, ['VIEW_COUNT = 4', ':variant="active"', 'aria-pressed', 'watch'])
  })
})

describe('M7 商品详情与购物车', () => {
  it('详情：面包屑 + 画廊 + 同类推荐 + 折叠技术溯源', () => {
    mustContain(productDetailSource, [
      'ElBreadcrumb', 'ProductGallery', '同类推荐',
      '技术溯源', 'ElCollapseItem', 'product.deducted', 'cart.add',
    ])
  })

  it('购物车：行级勾选 + 全选 + 仅结算已选合计', () => {
    mustContain(cartSource, [
      'ElCheckbox', 'cart.toggleAll()', 'cart.toggleSelect',
      'cart.selectedTotalAmount', '删除已选', '/checkout',
    ])
  })

  it('结算：模拟地址 / 优惠券 / 支付方式 + 只提交已勾选明细', () => {
    mustContain(checkoutSource, [
      '模拟 · 演示', 'addresses', 'demoDiscount', 'ElRadioGroup',
      'buildPlaceOrder(cart.selectedItems())', 'checkout-bar',
    ])
    // 演示项不得混入下单请求体，金额仍以后端为准。
    expect(checkoutSource).not.toContain('coupon: coupon')
    expect(checkoutSource).not.toContain('addressId: addressId')
  })
})

describe('M7 购买闭环与演示静态壳', () => {
  it('订单详情：状态步骤条 + 待支付引导 + 模拟发货', () => {
    mustContain(orderDetailSource, [
      'ElSteps', '待支付', '模拟发货 · 演示', 'shipOrder', '模拟支付 · 演示', '继续选购',
    ])
  })

  it('订单列表：去支付入口 + 待支付提示', () => {
    mustContain(ordersSource, ['去支付', 'pendingCount', 'firstPendingId'])
  })

  it('Member 静态壳：明确无用户系统、无鉴权、无本地持久化', () => {
    mustContain(memberSource, ['没有真实用户系统', '不提供注册与登录', '不收集'])
    expect(memberSource).not.toContain('localStorage.setItem')
    expect(memberSource).not.toContain('Bearer ')
    expect(memberSource).not.toContain('ElForm')
    expect(memberSource).not.toContain('api.')
  })
})
