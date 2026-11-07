import { describe, it, expect } from 'vitest'
import { routes, adminNav, DEMO_BANNER } from './routes'
import * as routeModule from './routes'

interface RawRoute {
  path: string
  component?: unknown
  redirect?: unknown
  children?: RawRoute[]
}
interface NavItemLike { path: string; title: string }

const raw = routes as unknown as RawRoute[]
const shopNav = (routeModule as unknown as Record<string, NavItemLike[]>).shopNav

describe('M6a 信息架构 — 用户端 7 路由 + 全局导航（SPEC v0.2 18.3）', () => {
  it('7 条用户端业务路由齐全', () => {
    const paths = raw.map((r) => r.path)
    for (const p of [
      '/', '/product/:sku', '/cart', '/checkout', '/orders', '/order/:id', '/order/:id/result',
    ]) {
      expect(paths).toContain(p)
    }
  })

  it('/order/:id/result 为兼容重定向（函数，保留 id 参数），不再挂页面组件', () => {
    const legacy = raw.find((r) => r.path === '/order/:id/result')
    expect(typeof legacy?.redirect).toBe('function')
    expect(legacy?.component).toBeUndefined()
  })

  it('/about 关于本项目路由存在', () => {
    expect(raw.map((r) => r.path)).toContain('/about')
  })

  it('shopNav 4 项且路径指向商城 / 我的订单 / 运维后台 / 关于本项目', () => {
    expect(Array.isArray(shopNav)).toBe(true)
    expect((Array.isArray(shopNav) ? shopNav : []).map((n) => n.path)).toEqual([
      '/', '/orders', '/admin/dashboard', '/about',
    ])
  })

  it('新增页面级 component 仍是 lazy 函数（dynamic import code-split）', () => {
    for (const p of ['/product/:sku', '/orders', '/order/:id', '/about']) {
      expect(typeof raw.find((r) => r.path === p)?.component).toBe('function')
    }
  })

  it('adminNav 仍 8 项，演示声明与未知路径回首页不变', () => {
    expect(adminNav).toHaveLength(8)
    expect(DEMO_BANNER).toContain('不涉及真实交易')
    expect(raw.find((r) => r.path === '/:pathMatch(.*)*')?.redirect).toBe('/')
  })
})
