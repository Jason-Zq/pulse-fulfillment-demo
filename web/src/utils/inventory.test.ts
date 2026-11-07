import { describe, it, expect } from 'vitest'
import { stockView } from './inventory'

describe('stockView — 库存三态聚合', () => {
  it('total = available + reserved + deducted，三态数值原样保留', () => {
    expect(stockView(5, 2, 3)).toMatchObject({
      available: 5,
      reserved: 2,
      deducted: 3,
      total: 10,
    })
  })

  it('可售为 0 → empty / danger / 售罄', () => {
    expect(stockView(0, 0, 0)).toMatchObject({ level: 'empty', type: 'danger', label: '售罄' })
  })

  it('可售低于阈值 → low / warning / 偏低', () => {
    expect(stockView(3, 0, 0)).toMatchObject({ level: 'low', type: 'warning', label: '偏低' })
  })

  it('可售达到阈值 → ok / success / 充足', () => {
    expect(stockView(10, 0, 0)).toMatchObject({ level: 'ok', type: 'success', label: '充足' })
  })

  it('自定义低库存阈值', () => {
    expect(stockView(5, 0, 0, 3)).toMatchObject({ level: 'ok' })
    expect(stockView(2, 0, 0, 3)).toMatchObject({ level: 'low' })
  })

  it('负值 / 非有限值裁剪为 0', () => {
    expect(stockView(-5, -1, 0)).toMatchObject({ available: 0, reserved: 0, total: 0, level: 'empty' })
    expect(stockView(Number.NaN, 1, 2).total).toBe(3)
  })
})
