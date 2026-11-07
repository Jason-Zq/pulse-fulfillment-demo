import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { api } from './client'
import { buildPlaceOrder } from '../utils/checkout'

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

describe('M6b 下单契约 — items[] 与单 SKU 双路径', () => {
  it('多明细请求体恰为 { items, idempotencyKey }，header 带幂等键', async () => {
    fetchMock.mockResolvedValue(makeResponse({ id: 1, orderNo: 'PO-M6B' }))
    const payload = {
      items: [
        { sku: 'SKU-1', quantity: 2 },
        { sku: 'SKU-2', quantity: 3 },
      ],
      idempotencyKey: 'multi-key',
    }

    await api.placeOrder(payload)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/orders')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual(payload)
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('multi-key')
  })

  it('单 SKU 请求体仍恰为 { sku, quantity, idempotencyKey }（132 条容器断言兼容）', async () => {
    fetchMock.mockResolvedValue(makeResponse({ id: 2, orderNo: 'PO-SINGLE' }))
    const prepared = buildPlaceOrder([{ sku: 'SKU-1', quantity: 1 }])

    await api.placeOrder(prepared)

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(JSON.parse(init.body as string)).toEqual({
      sku: 'SKU-1',
      quantity: 1,
      idempotencyKey: prepared.idempotencyKey,
    })
  })
})
