import { describe, it, expect, expectTypeOf } from 'vitest'
// ?raw 把 types.ts 当纯文本载入，对「字段名集合」做运行期静态契约断言（沿用 M5 做法）。
import typesSource from './types.ts?raw'
import type {
  MultiItemOrderPayload,
  OrderDetail,
  OrderEvidence,
  OrderQuery,
  OrderStatus,
  PlaceOrderItem,
  PlaceOrderPayload,
  Product,
} from './types'

describe('M6a 契约回归 — SPEC v0.2 新增字段（先锁契约，后端 M6b 才落地）', () => {
  it('types.ts 锁死 4 个可选字段，兼容 M4/M5 现有响应', () => {
    for (const token of [
      'version?: number',
      'items?: PlaceOrderItem[]',
      'orderNo?: string',
      'evidence?: OrderEvidence',
    ]) {
      expect(typesSource).toContain(token)
    }
  })

  it('Product.version / PlaceOrderPayload.items / OrderQuery.orderNo / OrderDetail.evidence 均为可选', () => {
    expectTypeOf<Product['version']>().toEqualTypeOf<number | undefined>()
    expectTypeOf<PlaceOrderPayload['items']>().toEqualTypeOf<PlaceOrderItem[] | undefined>()
    expectTypeOf<OrderQuery['orderNo']>().toEqualTypeOf<string | undefined>()
    expectTypeOf<OrderDetail['evidence']>().toEqualTypeOf<OrderEvidence | undefined>()
  })

  it('MultiItemOrderPayload 形状为 { items, idempotencyKey }，不含 sku/quantity', () => {
    const payload: MultiItemOrderPayload = {
      items: [{ sku: 'SKU-1', quantity: 2 }],
      idempotencyKey: 'k',
    }
    expect(Object.keys(payload).sort()).toEqual(['idempotencyKey', 'items'])
    expectTypeOf<MultiItemOrderPayload['items']>().toEqualTypeOf<PlaceOrderItem[]>()
  })

  it('OrderEvidence 五个字段名与契约表一致', () => {
    const evidence: OrderEvidence = {
      outboxEventId: 'e-1',
      outboxStatus: 'published',
      processedEvent: true,
      ledgerCount: 2,
      deadLetterCount: 0,
    }
    expect(Object.keys(evidence).sort()).toEqual([
      'deadLetterCount', 'ledgerCount', 'outboxEventId', 'outboxStatus', 'processedEvent',
    ])
  })

  it('OrderQuery 含 orderNo 且保留原有筛选项', () => {
    const query: OrderQuery = {
      orderNo: 'PO2026', status: 'Created' as OrderStatus, page: 1, pageSize: 20,
    }
    expect(query.orderNo).toBe('PO2026')
    expectTypeOf<OrderQuery['page']>().toEqualTypeOf<number | undefined>()
  })
})
