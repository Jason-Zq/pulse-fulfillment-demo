import { describe, it, expect } from 'vitest'
import { statusTag } from './statusTag'

describe('statusTag — OrderStatus → 中文标签 + el-tag type', () => {
  const cases: Array<[string, string, string]> = [
    ['Created', '待支付', 'info'],
    ['Paid', '待发货', 'primary'],
    ['Shipped', '已发货', 'primary'],
    ['Completed', '已完成', 'success'],
    ['Cancelled', '已取消', 'info'],
    ['Refunding', '退款中', 'warning'],
    ['Refunded', '已退款', 'danger'],
  ]

  for (const [status, label, type] of cases) {
    it(`${status} → ${label} / ${type}`, () => {
      expect(statusTag(status)).toEqual({ label, type })
    })
  }

  it('未知状态回退为原文 + info', () => {
    expect(statusTag('Weird')).toEqual({ label: 'Weird', type: 'info' })
  })

  it('空字符串也安全回退', () => {
    expect(statusTag('')).toEqual({ label: '', type: 'info' })
  })
})
