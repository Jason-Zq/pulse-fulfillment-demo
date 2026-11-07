import { describe, it, expect } from 'vitest'
import tourSource from './Tour.vue?raw'
import shopLayoutSource from '../layouts/ShopLayout.vue?raw'
import productsSource from './Products.vue?raw'
import { routes } from '../router/routes'

function mustContain(src: string, tokens: string[]): void {
  expect(src.length).toBeGreaterThan(0)
  for (const token of tokens) expect(src).toContain(token)
}

describe('M9 /tour 路由与页面静态契约', () => {
  it('/tour 注册为 lazy 路由', () => {
    const raw = routes as unknown as Array<{ path: string; component?: unknown }>
    const tour = raw.find((r) => r.path === '/tour')
    expect(tour).toBeDefined()
    expect(typeof tour?.component).toBe('function')
  })

  it('Tour.vue 复用真实数据源与既有纯函数，不写死任何实验数字', () => {
    mustContain(tourSource, [
      'api.getPipeline',
      'api.listExperiments',
      'parseLatestEvidence',
      'orderedPipelineNodes',
      'pipelineCountText',
      '暂无实测数据',
      'DEMO_BANNER',
      'ElCollapse',
      'generatedAtUtc',
      '复制',
      '/api/orders',
      'q.fulfillment',
      'processed_events',
    ])
    // 数字诚实：RPS / P99 会随机器负载波动，页面里不得出现任何一次真跑的数字。
    for (const n of ['75.97', '92.79', '83.45', '32.08', '22.18', '40.42']) {
      expect(tourSource).not.toContain(n)
    }
    expect(tourSource).not.toContain("from 'axios'")
    expect(tourSource).not.toContain('localStorage.setItem')
    // 文案会提到 SignalR 作为「没选的方案」，这里只禁止真实引入长连接依赖。
    expect(tourSource).not.toContain('@microsoft/signalr')
    expect(tourSource).not.toContain('new HubConnection')
  })

  it('追问 Q&A 覆盖 8 个高频问题', () => {
    for (const q of ['Outbox', 'TTL', 'DLX', '双保险', '轮询', '语义值', 'E3', '.NET 8']) {
      expect(tourSource).toContain(q)
    }
  })
})

describe('M9 入口接线', () => {
  it('ShopLayout 顶栏有「技术演示」入口', () => {
    mustContain(shopLayoutSource, ['to="/tour"', '技术演示', 'DEMO_BANNER'])
  })

  it('首页技术溯源折叠块有导览链接（不挤占消费首屏）', () => {
    mustContain(productsSource, ['to="/tour"', '演示导览', 'ElCollapse'])
  })
})
