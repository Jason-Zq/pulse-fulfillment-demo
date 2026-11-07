import { describe, it, expect } from 'vitest'
import { buildPlaceOrder } from './checkout'

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

describe('buildPlaceOrder — v0.2 单 SKU / 多明细双路径 + 幂等键', () => {
  it('多明细输出 { items, idempotencyKey }，不带 sku/quantity', () => {
    const prepared = buildPlaceOrder([
      { sku: 'S1', quantity: 2 },
      { sku: 'S2', quantity: 1 },
    ])
    expect(prepared).toEqual({
      items: [
        { sku: 'S1', quantity: 2 },
        { sku: 'S2', quantity: 1 },
      ],
      idempotencyKey: expect.any(String),
    })
    expect('sku' in prepared).toBe(false)
  })

  it('idempotencyKey 是 crypto.randomUUID() 生成的 UUID v4', () => {
    expect(buildPlaceOrder([]).idempotencyKey).toMatch(UUID_V4)
  })

  it('两次构造产生不同幂等键', () => {
    expect(buildPlaceOrder([]).idempotencyKey).not.toBe(buildPlaceOrder([]).idempotencyKey)
  })

  it('单行购物车保持 M1 单 SKU 形状（132 条容器断言兼容）', () => {
    const prepared = buildPlaceOrder([{ sku: 'SKU-9', quantity: 7 }])
    expect(prepared).toMatchObject({ sku: 'SKU-9', quantity: 7 })
    expect('items' in prepared).toBe(false)
  })
})
