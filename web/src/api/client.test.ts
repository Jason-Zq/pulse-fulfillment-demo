import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { api, ApiError, request } from './client'
import type { DailyReport, DeadLetter, OrderDetail, OrderList, Product } from './types'

function makeResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response
}

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('request — /api 前缀与 URL 构造', () => {
  it('给相对路径加 /api 前缀并返回解析后的 JSON', async () => {
    fetchMock.mockResolvedValue(makeResponse({ status: 'ok' }))
    const data = await request<{ status: string }>('/health')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/health')
    expect(data).toEqual({ status: 'ok' })
  })

  it('保留调用方传入的 query string，不重复拼 /api', async () => {
    fetchMock.mockResolvedValue(makeResponse({ items: [], total: 0, page: 1, pageSize: 20 }))
    await request<OrderList>('/orders?status=Paid&page=2')
    expect(fetchMock.mock.calls[0][0]).toBe('/api/orders?status=Paid&page=2')
  })

  it('POST 方法透传 method 与 JSON body', async () => {
    fetchMock.mockResolvedValue(makeResponse({ status: 'Paid' }))
    await request('/orders/o1/pay', { method: 'POST' })
    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect(init.method).toBe('POST')
  })
})

describe('request — 统一错误处理', () => {
  it('非 2xx 抛 ApiError 且带 status 与错误信息', async () => {
    fetchMock.mockResolvedValue(makeResponse('not found', 404))
    await expect(request('/orders/missing')).rejects.toBeInstanceOf(ApiError)
    await expect(request('/orders/missing')).rejects.toMatchObject({ status: 404 })
  })

  it('500 也归入 ApiError', async () => {
    fetchMock.mockResolvedValue(makeResponse('boom', 500))
    await expect(request('/products')).rejects.toMatchObject({ status: 500 })
  })

  it('网络异常包装成 status=0 的 ApiError', async () => {
    fetchMock.mockRejectedValue(new Error('network down'))
    await expect(request('/products')).rejects.toMatchObject({ status: 0 })
  })
})

describe('api — 各端点方法', () => {
  it('listProducts → GET /api/products', async () => {
    const products: Product[] = [
      { id: 1, sku: 'S1', name: '演示商品', price: 10, available: 5, reserved: 1, deducted: 0, status: 'OnSale', category: '数码电器' },
    ]
    fetchMock.mockResolvedValue(makeResponse(products))
    const data = await api.listProducts()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/products')
    expect(data[0].sku).toBe('S1')
  })

  it('placeOrder → POST /api/orders，body 含 sku / quantity / idempotencyKey，幂等键同时进 header', async () => {
    fetchMock.mockResolvedValue(
      makeResponse({ id: 1, orderNo: 'NO20261107', status: 'Created', message: '订单已创建，等待支付' }),
    )
    const prepared = { sku: 'S1', quantity: 2, idempotencyKey: 'uuid-123' }
    const data = await api.placeOrder(prepared)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/orders')
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('uuid-123')
    expect(JSON.parse(init.body as string)).toEqual({ sku: 'S1', quantity: 2, idempotencyKey: 'uuid-123' })
    expect(data.orderNo).toBe('NO20261107')
  })

  it('getOrder → GET /api/orders/{id}，透传 items 与 timeline 字段', async () => {
    const detail: OrderDetail = {
      id: 1,
      orderNo: 'NO1',
      status: 'Paid',
      totalAmount: 20,
      createdAtUtc: '2026-11-07T00:00:00Z',
      paidAtUtc: '2026-11-07T00:01:00Z',
      expiresAtUtc: '2026-11-07T00:15:00Z',
      items: [{ sku: 'S1', quantity: 2, unitPrice: 10 }],
      timeline: [{ status: 'Created', atUtc: '2026-11-07T00:00:00Z', note: '下单' }],
    }
    fetchMock.mockResolvedValue(makeResponse(detail))
    const data = await api.getOrder('o1')
    expect(fetchMock.mock.calls[0][0]).toBe('/api/orders/o1')
    expect(data.items[0].unitPrice).toBe(10)
    expect(data.timeline[0].status).toBe('Created')
    expect(data.paidAtUtc).toBe('2026-11-07T00:01:00Z')
  })

  it('payOrder / cancelOrder / shipOrder → 对应 POST 端点', async () => {
    fetchMock.mockResolvedValue(makeResponse({ status: 'Paid' }))
    await api.payOrder('o1')
    expect(fetchMock.mock.calls[0][0]).toBe('/api/orders/o1/pay')
    await api.cancelOrder('o1')
    expect(fetchMock.mock.calls[1][0]).toBe('/api/orders/o1/cancel')
    await api.shipOrder('o1')
    expect(fetchMock.mock.calls[2][0]).toBe('/api/orders/o1/ship')
  })

  it('listOrders → GET /api/orders?{query}，返回分页 DTO', async () => {
    fetchMock.mockResolvedValue(makeResponse({ items: [], total: 0, page: 3, pageSize: 20 }))
    const data = await api.listOrders('status=Paid&page=3&pageSize=20')
    expect(fetchMock.mock.calls[0][0]).toBe('/api/orders?status=Paid&page=3&pageSize=20')
    expect(data).toMatchObject({ page: 3, pageSize: 20, total: 0 })
  })

  it('getDailyReport → GET /api/reports/daily?date=，透传四方对账字段', async () => {
    const report: DailyReport = {
      statDate: '2026-11-07',
      orderCnt: 3,
      paidCnt: 1,
      cancelCnt: 1,
      gmv: 30,
      reconciliation: { orderAmount: 30, itemAmount: 30, ledgerReserveQty: 3, diff: 0 },
    }
    fetchMock.mockResolvedValue(makeResponse(report))
    const data = await api.getDailyReport('2026-11-07')
    expect(fetchMock.mock.calls[0][0]).toBe('/api/reports/daily?date=2026-11-07')
    expect(data.reconciliation.diff).toBe(0)
    expect(data.orderCnt).toBe(3)
  })

  it('listDeadLetters → 带/不带 status，透传 M3 字段', async () => {
    const dead: DeadLetter[] = [
      {
        id: 1, originalEventId: 'e1', payload: '{}', error: 'boom', retryCount: 3,
        status: 'pending', createdAtUtc: '2026-11-07T00:00:00Z', handledAtUtc: null,
      },
    ]
    fetchMock.mockResolvedValue(makeResponse(dead))
    await api.listDeadLetters('pending')
    expect(fetchMock.mock.calls[0][0]).toBe('/api/dead-letters?status=pending')
    await api.listDeadLetters()
    expect(fetchMock.mock.calls[1][0]).toBe('/api/dead-letters')
    const data = await api.listDeadLetters('pending')
    expect(data[0].originalEventId).toBe('e1')
  })

  it('retryDeadLetter → POST /api/dead-letters/{id}/retry', async () => {
    fetchMock.mockResolvedValue(makeResponse({ status: 'handled' }))
    await api.retryDeadLetter('d1')
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/dead-letters/d1/retry')
    expect(init.method).toBe('POST')
  })

  it('updatePrices → POST /api/products/prices 批量改价', async () => {
    fetchMock.mockResolvedValue(makeResponse({ updated: 2 }))
    await api.updatePrices([{ sku: 'S1', price: 12 }, { sku: 'S2', price: 8 }])
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/products/prices')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toHaveLength(2)
  })
})
