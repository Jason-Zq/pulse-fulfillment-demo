import type {
  AlertRecord,
  DailyReport,
  DashboardSnapshot,
  DeadLetter,
  ExperimentResult,
  InjectRequest,
  InjectResult,
  LabState,
  OrderDetail,
  OrderList,
  PipelineSnapshot,
  PlaceOrderBody,
  PlaceOrderResult,
  PriceUpdate,
  Product,
  ResetResult,
  SilenceResult,
  StatusResult,
  TrafficRequest,
  TrafficResult,
} from './types'

const API_BASE = '/api'

/** 统一错误类型：HTTP 非 2xx 与网络异常都归一为 ApiError。 */
export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(message: string, status: number, body: unknown = undefined) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

/** 底层 fetch 封装：自动加 /api 前缀、解析 JSON、归一化错误。 */
export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const url = `${API_BASE}${path}`
  let res: Response
  try {
    res = await fetch(url, init)
  } catch (err) {
    throw new ApiError(err instanceof Error ? err.message : '网络请求失败', 0)
  }

  if (!res.ok) {
    const detail = await res.text().catch(() => '')
    throw new ApiError(`HTTP ${res.status}`, res.status, detail)
  }

  return (await res.json()) as T
}

function jsonInit(method: string, body?: unknown, extraHeaders: Record<string, string> = {}): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json', ...extraHeaders },
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  }
}

/** 全站 API 客户端。方法名与 DTO 契约表一一对应。 */
export const api = {
  listProducts: (): Promise<Product[]> => request<Product[]>('/products'),

  // M6b：单 SKU 与 items[] 双路径。请求体按形状二选一，绝不混发（后端同传 → 400）。
  placeOrder: (payload: PlaceOrderBody): Promise<PlaceOrderResult> => {
    const body = 'sku' in payload
      ? { sku: payload.sku, quantity: payload.quantity, idempotencyKey: payload.idempotencyKey }
      : { items: payload.items, idempotencyKey: payload.idempotencyKey }
    return request<PlaceOrderResult>(
      '/orders',
      jsonInit('POST', body, { 'Idempotency-Key': payload.idempotencyKey }),
    )
  },

  getOrder: (id: string | number): Promise<OrderDetail> =>
    request<OrderDetail>(`/orders/${encodeURIComponent(id)}`),

  payOrder: (id: string | number): Promise<StatusResult> =>
    request<StatusResult>(`/orders/${encodeURIComponent(id)}/pay`, jsonInit('POST')),

  cancelOrder: (id: string | number): Promise<StatusResult> =>
    request<StatusResult>(`/orders/${encodeURIComponent(id)}/cancel`, jsonInit('POST')),

  shipOrder: (id: string | number): Promise<StatusResult> =>
    request<StatusResult>(`/orders/${encodeURIComponent(id)}/ship`, jsonInit('POST')),

  listOrders: (query: string): Promise<OrderList> =>
    request<OrderList>(`/orders${query ? `?${query}` : ''}`),

  getDailyReport: (date: string): Promise<DailyReport> =>
    request<DailyReport>(`/reports/daily?date=${encodeURIComponent(date)}`),

  listDeadLetters: (status?: string): Promise<DeadLetter[]> =>
    request<DeadLetter[]>(`/dead-letters${status ? `?status=${encodeURIComponent(status)}` : ''}`),

  retryDeadLetter: (id: string | number): Promise<StatusResult> =>
    request<StatusResult>(`/dead-letters/${encodeURIComponent(id)}/retry`, jsonInit('POST')),

  updatePrices: (items: PriceUpdate[]): Promise<{ updated: number }> =>
    request<{ updated: number }>('/products/prices', jsonInit('POST', items)),

  // ---- M5：仪表盘 / 告警 / 实验台 ----
  getDashboard: (): Promise<DashboardSnapshot> =>
    request<DashboardSnapshot>('/dashboard'),

  // M6c：链路泳道 7 节点真实计数（队列不可用节点 count=-1 / available=false）。
  getPipeline: (): Promise<PipelineSnapshot> =>
    request<PipelineSnapshot>('/pipeline'),

  listAlerts: (limit = 50): Promise<AlertRecord[]> =>
    request<AlertRecord[]>(`/alerts?limit=${encodeURIComponent(limit)}`),

  silenceAlert: (key: string, minutes: number): Promise<SilenceResult> =>
    request<SilenceResult>(`/alerts/${encodeURIComponent(key)}/silence`, jsonInit('POST', { minutes })),

  getLabState: (): Promise<LabState> =>
    request<LabState>('/lab/state'),

  runTraffic: (payload: TrafficRequest): Promise<TrafficResult> =>
    request<TrafficResult>('/lab/traffic', jsonInit('POST', payload)),

  inject: (payload: InjectRequest): Promise<InjectResult> =>
    request<InjectResult>('/lab/inject', jsonInit('POST', payload)),

  resetLab: (): Promise<ResetResult> =>
    request<ResetResult>('/lab/reset', jsonInit('POST')),

  listExperiments: (): Promise<ExperimentResult[]> =>
    request<ExperimentResult[]>('/experiments'),

  runExperiment: (name: 'E1' | 'E2' | 'E3'): Promise<ExperimentResult> =>
    request<ExperimentResult>('/experiments/run', jsonInit('POST', { name })),
}
