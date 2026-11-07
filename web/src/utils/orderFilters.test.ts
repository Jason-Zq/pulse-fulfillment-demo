import { describe, it, expect } from 'vitest'
import { buildOrderQuery } from './orderFilters'

describe('buildOrderQuery — 筛选条件 → query string', () => {
  it('空条件只保留默认分页', () => {
    expect(buildOrderQuery({})).toBe('page=1&pageSize=20')
  })

  it('裁剪空字符串 / undefined 条件', () => {
    expect(buildOrderQuery({ status: '', sku: undefined, from: '', to: '' })).toBe('page=1&pageSize=20')
  })

  it('保留 status / sku / 日期范围 from-to', () => {
    const q = buildOrderQuery({ status: 'Paid', sku: 'S1', from: '2026-11-01', to: '2026-11-07' })
    expect(q).toContain('status=Paid')
    expect(q).toContain('sku=S1')
    expect(q).toContain('from=2026-11-01')
    expect(q).toContain('to=2026-11-07')
  })

  it('特殊字符按 URL 规则编码', () => {
    expect(buildOrderQuery({ sku: 'A B&C' })).toContain('sku=A+B%26C')
  })

  it('自定义分页生效', () => {
    const q = buildOrderQuery({ page: 3, pageSize: 50 })
    expect(q).toContain('page=3')
    expect(q).toContain('pageSize=50')
  })

  it('非法分页（0 / 负数）回退到默认', () => {
    const q = buildOrderQuery({ page: 0, pageSize: -5 })
    expect(q).toContain('page=1')
    expect(q).toContain('pageSize=20')
  })

  it('参数顺序稳定：status,sku,from,to,page,pageSize', () => {
    const q = buildOrderQuery({ page: 2, to: 't', from: 'f', sku: 'S', status: 'Created', pageSize: 10 })
    expect(q).toBe('status=Created&sku=S&from=f&to=t&page=2&pageSize=10')
  })

  // M6b：GET /api/orders 新增 orderNo 模糊匹配，「我的订单」页要能按订单号搜。
  it('M6b：orderNo 非空时写入 query，空值不写入', () => {
    expect(buildOrderQuery({ orderNo: 'PO20261107' })).toContain('orderNo=PO20261107')
    expect(buildOrderQuery({ orderNo: '' })).toBe('page=1&pageSize=20')
  })

  it('M6b：orderNo 与状态组合时参数顺序稳定', () => {
    expect(buildOrderQuery({ orderNo: 'PO 1', status: 'Created' })).toBe(
      'status=Created&orderNo=PO+1&page=1&pageSize=20',
    )
  })
})
