import { describe, it, expect, expectTypeOf, vi, beforeEach, afterEach } from 'vitest'
import { api } from './client'
import type {
  AlertLevel,
  AlertRecord,
  DailyReport,
  DashboardSnapshot,
  DeadLetter,
  ExperimentResult,
  InjectRequest,
  InjectResult,
  InjectType,
  LabState,
  OrderDetail,
  OrderListItem,
  PlaceOrderResult,
  Product,
  ResetResult,
  SilenceResult,
  TrafficRequest,
  TrafficResult,
} from './types'
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

describe('契约回归 — POST /api/orders 请求体（后端从 body 读 sku / quantity / idempotencyKey）', () => {
  it('placeOrder 发出的 body 恰为 { sku, quantity, idempotencyKey }，幂等键同时在 body 与 header', async () => {
    fetchMock.mockResolvedValue(
      makeResponse({
        id: 1,
        orderNo: 'PO20261107',
        status: 'Created',
        message: '订单已创建，等待支付',
      }),
    )

    const prepared = buildPlaceOrder([{ sku: 'SKU-1', quantity: 2 }])
    const data = await api.placeOrder(prepared)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/orders')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({
      sku: 'SKU-1',
      quantity: 2,
      idempotencyKey: prepared.idempotencyKey,
    })
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe(prepared.idempotencyKey)
    expect(data).toMatchObject({ id: 1, orderNo: 'PO20261107' })
  })

  it('buildPlaceOrder 多明细输出 { items, idempotencyKey }（SPEC v0.2 / M6b）', () => {
    const prepared = buildPlaceOrder([
      { sku: 'SKU-1', quantity: 2 },
      { sku: 'SKU-2', quantity: 5 },
    ])
    expect(prepared).toEqual({
      items: [
        { sku: 'SKU-1', quantity: 2 },
        { sku: 'SKU-2', quantity: 5 },
      ],
      idempotencyKey: expect.any(String),
    })
  })
})

describe('契约回归 — id 类型与商品字段（后端 long → JSON number）', () => {
  it('Product 字段齐全且 id 为 number', () => {
    const product: Product = {
      id: 1,
      sku: 'SKU-1',
      name: '演示商品',
      price: 99.5,
      available: 98,
      reserved: 2,
      deducted: 0,
      status: 'active',
      category: '数码电器',
    }
    // M7：后端 Product.Category 外显后，GET /api/products 由 8 字段扩展到 9 字段。
    expect(Object.keys(product).sort()).toEqual([
      'available', 'category', 'deducted', 'id', 'name', 'price', 'reserved', 'sku', 'status',
    ])
    expect(typeof product.id).toBe('number')
    expectTypeOf(product.id).toEqualTypeOf<number>()
  })

  it('OrderListItem / OrderDetail / DeadLetter / PlaceOrderResult 的 id 均为 number', () => {
    expectTypeOf<OrderListItem['id']>().toEqualTypeOf<number>()
    expectTypeOf<OrderDetail['id']>().toEqualTypeOf<number>()
    expectTypeOf<DeadLetter['id']>().toEqualTypeOf<number>()
    expectTypeOf<PlaceOrderResult['id']>().toEqualTypeOf<number>()
  })

  it('getOrder 接受 number id 并拼出 /api/orders/{id}', async () => {
    fetchMock.mockResolvedValue(makeResponse({ id: 7, orderNo: 'NO7' }))
    await api.getOrder(7)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/orders/7')
  })

  it('payOrder / cancelOrder / shipOrder / retryDeadLetter 接受 number id', async () => {
    fetchMock.mockResolvedValue(makeResponse({ status: 'Paid' }))
    await api.payOrder(1)
    await api.cancelOrder(2)
    await api.shipOrder(3)
    await api.retryDeadLetter(4)
    expect(fetchMock.mock.calls.map((call) => call[0])).toEqual([
      '/api/orders/1/pay',
      '/api/orders/2/cancel',
      '/api/orders/3/ship',
      '/api/dead-letters/4/retry',
    ])
  })
})

// ---------------------------------------------------------------------------
// M5 契约回归：9 个新方法 + 全部新 DTO（字段名/类型严格对齐 docs/PLAN.md M5 DTO 表）
// ---------------------------------------------------------------------------

describe('M5 契约回归 — 9 个新方法存在', () => {
  it('api 暴露 getDashboard / listAlerts / silenceAlert / getLabState / runTraffic / inject / resetLab / listExperiments / runExperiment', () => {
    for (const name of [
      'getDashboard', 'listAlerts', 'silenceAlert', 'getLabState', 'runTraffic',
      'inject', 'resetLab', 'listExperiments', 'runExperiment',
    ] as const) {
      expect(typeof (api as unknown as Record<string, unknown>)[name]).toBe('function')
    }
  })
})

describe('M5 契约回归 — URL 与请求体形状', () => {
  it('getDashboard → GET /api/dashboard', async () => {
    fetchMock.mockResolvedValue(makeResponse({ generatedAtUtc: '2026-11-07T00:00:00Z' }))
    await api.getDashboard()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/dashboard')
  })

  it('listAlerts → GET /api/alerts?limit=50（可覆盖 limit）', async () => {
    fetchMock.mockResolvedValue(makeResponse([]))
    await api.listAlerts()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/alerts?limit=50')
    await api.listAlerts(10)
    expect(fetchMock.mock.calls[1][0]).toBe('/api/alerts?limit=10')
  })

  it('silenceAlert → POST /api/alerts/{key}/silence，body 恰为 { minutes }', async () => {
    fetchMock.mockResolvedValue(
      makeResponse({ alertKey: 'low-stock', silenced: true, silencedUntilUtc: null }),
    )
    await api.silenceAlert('low stock', 30)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/alerts/low%20stock/silence')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ minutes: 30 })
  })

  it('getLabState → GET /api/lab/state', async () => {
    fetchMock.mockResolvedValue(makeResponse({ consumerEnabled: true }))
    await api.getLabState()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/lab/state')
  })

  it('runTraffic → POST /api/lab/traffic，body 与请求 DTO 一致（含可选 concurrency）', async () => {
    fetchMock.mockResolvedValue(makeResponse({ experimentId: 1 }))
    const payload: TrafficRequest = { sku: 'SKU-1', count: 20, rps: 5, quantity: 1, concurrency: 4 }
    await api.runTraffic(payload)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/lab/traffic')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual(payload)
  })

  it('inject → POST /api/lab/inject，body 恰为 { type, count?, orderNo? }', async () => {
    fetchMock.mockResolvedValue(makeResponse({ type: 'stop_consumer', applied: true, detail: 'ok' }))
    const payload: InjectRequest = { type: 'stop_consumer' }
    await api.inject(payload)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/lab/inject')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ type: 'stop_consumer' })
  })

  it('resetLab → POST /api/lab/reset，无请求体', async () => {
    fetchMock.mockResolvedValue(
      makeResponse({ status: 'ok', productsSeeded: 2, clearedAtUtc: '2026-11-07T00:00:00Z' }),
    )
    await api.resetLab()
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/lab/reset')
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
  })

  it('listExperiments → GET /api/experiments', async () => {
    fetchMock.mockResolvedValue(makeResponse([]))
    await api.listExperiments()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/experiments')
  })

  it('runExperiment → POST /api/experiments/run，body 恰为 { name }', async () => {
    fetchMock.mockResolvedValue(
      makeResponse({
        id: 1, name: 'E2', params: '{}', result: '{}',
        ranAtUtc: '2026-11-07T00:00:00Z', passed: true,
      }),
    )
    await api.runExperiment('E2')
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/experiments/run')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ name: 'E2' })
  })
})

describe('M5 契约回归 — DTO 字段名 / 类型（vue-tsc 编译期 + expectTypeOf 运行期双重校验）', () => {
  it('DashboardSnapshot 完整字面量可赋值（所有嵌套字段名/类型对齐契约表）', () => {
    const snapshot: DashboardSnapshot = {
      generatedAtUtc: '2026-11-07T00:00:00Z',
      today: { orderCnt: 10, paidCnt: 4, cancelCnt: 1, gmv: 123.45 },
      inventory: { skuCount: 2, lowStockCount: 1, totalAvailable: 100, lowStockThreshold: 10 },
      queues: { fulfillment: 1, orderTimeout: 2, fulfillmentRetry: 0, dlq: 0, available: true },
      outboxPending: 3,
      deadLetterPending: 0,
      alertsActive: 1,
      controls: { consumerEnabled: true, publisherEnabled: true, failureInjectionEnabled: false },
      source: { database: 'sqlite', redis: 'not_configured', rabbitmq: 'not_configured' },
    }
    expect(snapshot.queues.available).toBe(true)
    expect(snapshot.today.gmv).toBe(123.45)
  })

  it('AlertRecord / SilenceResult / LabState / TrafficResult / InjectResult / ResetResult / ExperimentResult 字面量可赋值', () => {
    const alert: AlertRecord = {
      id: 1, alertKey: 'low-stock', level: 'warning', message: '库存偏低',
      createdAtUtc: '2026-11-07T00:00:00Z', silenced: false, silencedUntilUtc: null,
    }
    const silence: SilenceResult = {
      alertKey: 'low-stock', silenced: true, silencedUntilUtc: '2026-11-07T01:00:00Z',
    }
    const state: LabState = {
      consumerEnabled: true, publisherEnabled: true, failureInjectionEnabled: false,
      database: 'sqlite', redisConfigured: false, rabbitConfigured: false,
    }
    const traffic: TrafficResult = {
      requested: 20, created: 20, failed: 0, elapsedMs: 4000,
      throughputRps: 5, p99Ms: 123.4, experimentId: 7,
    }
    const injected: InjectResult = { type: 'duplicate', applied: true, detail: '注入 1 条' }
    const reset: ResetResult = {
      status: 'ok', productsSeeded: 2, clearedAtUtc: '2026-11-07T00:00:00Z',
    }
    const exp: ExperimentResult = {
      id: 3, name: 'E1', params: '{"count":20}', result: '{"passed":true}',
      ranAtUtc: '2026-11-07T00:00:00Z', passed: true,
    }
    expect(alert.id).toBe(1)
    expect(silence.silenced).toBe(true)
    expect(state.redisConfigured).toBe(false)
    expect(traffic.experimentId).toBe(7)
    expect(injected.applied).toBe(true)
    expect(reset.productsSeeded).toBe(2)
    expect(exp.name).toBe('E1')
  })

  it('所有 id 均为 number（后端 long → JSON number）', () => {
    expectTypeOf<AlertRecord['id']>().toEqualTypeOf<number>()
    expectTypeOf<ExperimentResult['id']>().toEqualTypeOf<number>()
    expectTypeOf<TrafficResult['experimentId']>().toEqualTypeOf<number>()
    expectTypeOf<DailyReport['reconciliation']['diff']>().toEqualTypeOf<number>()
  })

  it('ExperimentResult.params / result 是 string（JSON 字符串，不是对象）', () => {
    expectTypeOf<ExperimentResult['params']>().toEqualTypeOf<string>()
    expectTypeOf<ExperimentResult['result']>().toEqualTypeOf<string>()
  })

  it('AlertLevel / InjectType 为契约枚举，DailyReport 新增 persisted', () => {
    expectTypeOf<AlertRecord['level']>().toEqualTypeOf<AlertLevel>()
    expectTypeOf<InjectRequest['type']>().toEqualTypeOf<InjectType>()
    expectTypeOf<DailyReport['persisted']>().toEqualTypeOf<boolean | undefined>()
    expectTypeOf<DashboardSnapshot['queues']['available']>().toEqualTypeOf<boolean>()
    expectTypeOf<SilenceResult['silencedUntilUtc']>().toEqualTypeOf<string | null>()
  })
})
