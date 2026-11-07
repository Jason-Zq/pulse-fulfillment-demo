import { describe, it, expect } from 'vitest'
import { adminNav } from '../router/routes'
import { ADMIN_PAGE_META, adminPageMeta } from './adminEvidence'

describe('M6c 后台页技术证据 — 8 页唯一真源', () => {
  it('8 个后台路由都有对应元数据，路径与 adminNav 完全一致', () => {
    expect(Object.keys(ADMIN_PAGE_META).sort()).toEqual(adminNav.map((n) => n.path).sort())
  })

  it('每页都有标题、面包屑、至少一个中间件与真实键表名', () => {
    for (const [path, meta] of Object.entries(ADMIN_PAGE_META)) {
      expect(meta.title.length, path).toBeGreaterThan(0)
      expect(meta.breadcrumb[0], path).toBe('运营后台')
      expect(meta.middlewares.length, path).toBeGreaterThan(0)
      expect(meta.keys.length, path).toBeGreaterThan(0)
      expect(meta.description.length, path).toBeGreaterThan(0)
    }
  })

  it('未知路径安全回退，不抛异常、不返回空证据', () => {
    const meta = adminPageMeta('/admin/not-exist')
    expect(meta.title.length).toBeGreaterThan(0)
    expect(meta.middlewares.length).toBeGreaterThan(0)
  })

  it('仪表盘与实验台带复现入口，指向真实存在的后台路径', () => {
    for (const path of ['/admin/dashboard', '/admin/lab']) {
      const meta = ADMIN_PAGE_META[path]
      expect(meta.reproduce, path).toBeDefined()
      expect(meta.reproduce!.path.startsWith('/admin/'), path).toBe(true)
    }
  })
})
