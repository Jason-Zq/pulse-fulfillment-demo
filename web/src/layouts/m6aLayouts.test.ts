import { describe, it, expect } from 'vitest'
// 与 M5 的 m5Pages.test.ts 一致：?raw 把 .vue 当纯文本载入，避免引入 node:fs（项目未装 @types/node）。
import shopLayoutSource from './ShopLayout.vue?raw'
import adminHeaderSource from './AdminHeader.vue?raw'
import techEvidenceSource from '../components/TechEvidenceCard.vue?raw'

function mustContain(src: string, tokens: string[]): void {
  expect(src.length).toBeGreaterThan(0)
  for (const token of tokens) expect(src).toContain(token)
}

describe('M6a 布局骨架静态契约 — 界面本身就是技术叙事', () => {
  it('ShopLayout.vue：shopNav 全局导航 + 购物车角标 + 常驻演示声明 + RouterView', () => {
    mustContain(shopLayoutSource, [
      'shopNav', 'useCartStore', 'totalCount', 'DEMO_BANNER', 'RouterView', 'ElMenu',
    ])
  })

  it('AdminHeader.vue：三中间件状态取 /api/dashboard 的 source，不可用显示「—」', () => {
    mustContain(adminHeaderSource, [
      'getDashboard', 'source', 'database', 'redis', 'rabbitmq', '—', 'DEMO_BANNER',
    ])
  })

  it('TechEvidenceCard.vue：中间件 / 真实键表名 / 一键复现入口三要素', () => {
    mustContain(techEvidenceSource, [
      'middlewares', 'keys', 'reproduce', 'ElTag', 'RouterLink',
    ])
  })
})
