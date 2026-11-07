import { describe, it, expect } from 'vitest'
import dashboard from './Dashboard.vue?raw'
import alerts from './Alerts.vue?raw'
import lab from './Lab.vue?raw'
import orders from './Orders.vue?raw'
import products from './Products.vue?raw'
import fulfillment from './Fulfillment.vue?raw'
import reports from './Reports.vue?raw'
import deadLetters from './DeadLetters.vue?raw'

const pages: Array<[string, string]> = [
  ['/admin/dashboard', dashboard],
  ['/admin/alerts', alerts],
  ['/admin/lab', lab],
  ['/admin/orders', orders],
  ['/admin/products', products],
  ['/admin/fulfillment', fulfillment],
  ['/admin/reports', reports],
  ['/admin/dead-letters', deadLetters],
]

describe('M6c 后台 8 页 — 空态与操作反馈统一', () => {
  it('8 页都有明确的 ElEmpty 空态', () => {
    for (const [path, src] of pages) {
      expect(src, path).toContain('ElEmpty')
    }
  })

  it('8 页都有 ElMessage 操作反馈', () => {
    for (const [path, src] of pages) {
      expect(src, path).toContain('ElMessage')
    }
  })

  it('告警 / 实验台 / 死信台 / 商品页接 3 秒轮询（写后立即刷新）', () => {
    for (const [path, src] of pages.filter(([p]) =>
      ['/admin/alerts', '/admin/lab', '/admin/products', '/admin/dead-letters'].includes(p))) {
      expect(src, path).toContain('usePolling')
    }
  })
})
