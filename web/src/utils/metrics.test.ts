import { describe, it, expect } from 'vitest'
import { clipWindow, alignDailyReport, formatAmount } from './metrics'
import type { DailyReport } from '../api/types'

describe('clipWindow — 指标窗口裁剪', () => {
  it('裁剪到最近 N 个点', () => {
    expect(clipWindow([1, 2, 3, 4, 5], 3)).toEqual([3, 4, 5])
  })

  it('窗口大于序列长度时全保留', () => {
    expect(clipWindow([1, 2], 5)).toEqual([1, 2])
  })

  it('窗口 <= 0 返回空', () => {
    expect(clipWindow([1, 2, 3], 0)).toEqual([])
    expect(clipWindow([1, 2, 3], -1)).toEqual([])
  })
})

describe('alignDailyReport — 日报数值对齐', () => {
  it('完整报告字段原样保留', () => {
    const full: DailyReport = {
      statDate: '2026-11-07',
      orderCnt: 3,
      paidCnt: 1,
      cancelCnt: 1,
      gmv: 30,
      reconciliation: { orderAmount: 30, itemAmount: 30, ledgerReserveQty: 3, diff: 0 },
    }
    expect(alignDailyReport(full)).toEqual(full)
  })

  it('null 输入返回全 0 且 diff=0', () => {
    const r = alignDailyReport(null)
    expect(r.orderCnt).toBe(0)
    expect(r.gmv).toBe(0)
    expect(r.reconciliation.diff).toBe(0)
    expect(r.statDate).toBe('')
  })

  it('部分字段补齐，不丢已有值', () => {
    const r = alignDailyReport({ gmv: 100 } as Partial<DailyReport>)
    expect(r.gmv).toBe(100)
    expect(r.paidCnt).toBe(0)
    expect(r.reconciliation.ledgerReserveQty).toBe(0)
  })

  it('reconciliation 缺 diff 时按 orderAmount - itemAmount 计算', () => {
    const r = alignDailyReport({
      reconciliation: { orderAmount: 30, itemAmount: 25, ledgerReserveQty: 1 } as DailyReport['reconciliation'],
    } as Partial<DailyReport>)
    expect(r.reconciliation.diff).toBe(5)
    expect(r.reconciliation.ledgerReserveQty).toBe(1)
  })
})

describe('formatAmount — 金额展示', () => {
  it('保留两位小数', () => {
    expect(formatAmount(12.5)).toBe('12.50')
  })

  it('空值 → 0.00', () => {
    expect(formatAmount(undefined)).toBe('0.00')
    expect(formatAmount(null)).toBe('0.00')
  })
})
