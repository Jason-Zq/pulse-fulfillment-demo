import { describe, it, expect } from 'vitest'
import productsSource from './Products.vue?raw'
import productDetailSource from './ProductDetail.vue?raw'
import cartSource from './Cart.vue?raw'
import checkoutSource from './Checkout.vue?raw'
import ordersSource from './Orders.vue?raw'
import orderDetailSource from './OrderDetail.vue?raw'
import appSource from '../App.vue?raw'

describe('M6b 商城页静态契约 — 成熟界面本身就是技术叙事', () => {
  it('首页：定位 + 三中间件 + 真实实验证据 + 三入口 + 商品卡片网格', () => {
    for (const token of [
      '订单履约中台', 'MySQL', 'Redis', 'RabbitMQ',
      'parseLatestEvidence', 'listExperiments', '暂无实测数据',
      '/admin/dashboard', '/admin/lab', 'product-grid',
    ]) {
      expect(productsSource).toContain(token)
    }
    expect(productsSource).not.toContain('75.97')
  })

  it('商品详情：库存三态 + version + 加购', () => {
    for (const token of ['available', 'reserved', 'deducted', 'version', 'cart.add']) {
      expect(productDetailSource).toContain(token)
    }
  })

  it('购物车：多行、小计、去结算', () => {
    for (const token of ['cart.lines', 'totalAmount', '/checkout']) {
      expect(cartSource).toContain(token)
    }
  })

  it('结算页：多明细确认，删除「仅结算第一行」提示', () => {
    expect(checkoutSource).not.toContain('仅结算购物车第一行')
    for (const token of ['buildPlaceOrder', 'idempotencyKey', 'placeOrder']) {
      expect(checkoutSource).toContain(token)
    }
  })

  it('我的订单：订单号搜索 + 状态筛选 + 取消', () => {
    for (const token of ['orderNo', 'listOrders', 'cancelOrder']) {
      expect(ordersSource).toContain(token)
    }
  })

  it('订单详情：显式「模拟支付 · 演示」按钮 + 3 秒轮询 + 链路证据卡', () => {
    for (const token of [
      '模拟支付 · 演示', 'payOrder', 'getOrder', '3000', 'TechEvidenceCard',
    ]) {
      expect(orderDetailSource).toContain(token)
    }
    expect(orderDetailSource).not.toContain('onMounted(async () => {\n  try {\n    await api.payOrder')
  })

  it('App.vue 接线 ShopLayout，后台路径仍走原 RouterView', () => {
    for (const token of ['ShopLayout', 'RouterView', 'isAdmin']) {
      expect(appSource).toContain(token)
    }
  })
})
