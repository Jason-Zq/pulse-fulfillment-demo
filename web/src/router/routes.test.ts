import { describe, it, expect } from 'vitest'
import { routes, adminNav, DEMO_BANNER } from './routes'

interface RawRoute {
  path: string
  component?: unknown
  redirect?: unknown
  children?: RawRoute[]
}

// 仅用于结构断言，避免测试内操作 vue-router 的联合类型。
const raw = routes as unknown as RawRoute[]

function adminChildren(): RawRoute[] {
  return raw.find((r) => r.path === '/admin')?.children ?? []
}

describe('routes — 用户端 + 后台完整路由', () => {
  it('用户端 4 条路由齐全', () => {
    const paths = raw.map((r) => r.path)
    expect(paths).toContain('/')
    expect(paths).toContain('/cart')
    expect(paths).toContain('/checkout')
    expect(paths).toContain('/order/:id/result')
  })

  it('后台 8 个页面 + 默认重定向齐全（含 M5 的 dashboard / alerts / lab）', () => {
    const children = adminChildren()
    const paths = children.map((c) => c.path)
    for (const p of [
      'dashboard', 'alerts', 'lab',
      'orders', 'products', 'fulfillment', 'reports', 'dead-letters',
    ]) {
      expect(paths).toContain(p)
    }
    expect(paths.filter((p) => p !== '')).toHaveLength(8)
    for (const p of ['dashboard', 'alerts', 'lab']) {
      expect(typeof children.find((c) => c.path === p)?.component).toBe('function')
    }
  })

  it('后台默认路径重定向到订单页', () => {
    expect(adminChildren().find((c) => c.path === '')?.redirect).toBe('/admin/orders')
  })

  it('所有页面级 component 都是 lazy 函数（dynamic import code-split）', () => {
    const lazies: unknown[] = []
    for (const r of raw) {
      if (r.component) lazies.push(r.component)
      for (const c of r.children ?? []) if (c.component) lazies.push(c.component)
    }
    expect(lazies.length).toBeGreaterThanOrEqual(9)
    for (const c of lazies) expect(typeof c).toBe('function')
  })

  it('后台导航 8 项，dashboard 居首、原 M4 五项保持原顺序', () => {
    expect(adminNav).toHaveLength(8)
    expect(adminNav.map((n) => n.path)).toEqual([
      '/admin/dashboard',
      '/admin/alerts',
      '/admin/lab',
      '/admin/orders',
      '/admin/products',
      '/admin/fulfillment',
      '/admin/reports',
      '/admin/dead-letters',
    ])
  })

  it('全站演示声明固定文案', () => {
    expect(DEMO_BANNER).toContain('演示环境')
    expect(DEMO_BANNER).toContain('全部数据为模拟')
    expect(DEMO_BANNER).toContain('不涉及真实交易')
  })

  it('未知路径重定向到首页', () => {
    expect(raw.find((r) => r.path === '/:pathMatch(.*)*')?.redirect).toBe('/')
  })
})
