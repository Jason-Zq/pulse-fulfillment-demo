import { describe, it, expect } from 'vitest'
import layoutSource from './AdminLayout.vue?raw'

describe('M6c AdminLayout — 全局健康条 + 面包屑 + 每页技术证据', () => {
  it('接线 AdminHeader / ElBreadcrumb / TechEvidenceCard', () => {
    for (const token of [
      'AdminHeader', 'ElBreadcrumb', 'ElBreadcrumbItem', 'TechEvidenceCard',
      'adminPageMeta', 'middlewares', 'keys', 'reproduce', 'RouterView',
    ]) {
      expect(layoutSource).toContain(token)
    }
  })

  it('技术证据按当前路由取元数据，不写死某一页', () => {
    expect(layoutSource).toContain('route.path')
    expect(layoutSource).toContain('pageMeta')
  })

  it('演示声明只保留一处（AdminHeader 提供），侧栏不重复渲染', () => {
    expect(layoutSource).not.toContain('class="demo-note"')
  })
})
