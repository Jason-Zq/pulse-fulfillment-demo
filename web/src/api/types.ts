// 前后端唯一接口约定：字段名严格对齐 docs/PLAN.md 的「DTO 契约」表，不可随意改动。

export type OrderStatus =
  | 'Created'
  | 'Paid'
  | 'Shipped'
  | 'Completed'
  | 'Cancelled'
  | 'Refunding'
  | 'Refunded'

/** GET /api/orders 列表项 */
export interface OrderListItem {
  id: number
  orderNo: string
  status: OrderStatus
  totalAmount: number
  createdAtUtc: string
  expiresAtUtc: string
  sku: string
}

/** GET /api/orders 响应 */
export interface OrderList {
  items: OrderListItem[]
  total: number
  page: number
  pageSize: number
}

/** GET /api/orders/{id} 明细行 */
export interface OrderItemDetail {
  sku: string
  quantity: number
  unitPrice: number
}

/** GET /api/orders/{id} 状态时间线 */
export interface OrderTimelineEntry {
  status: OrderStatus
  atUtc: string
  note: string
}

/** GET /api/orders/{id} 响应 */
export interface OrderDetail {
  id: number
  orderNo: string
  status: OrderStatus
  totalAmount: number
  createdAtUtc: string
  paidAtUtc: string | null
  expiresAtUtc: string
  items: OrderItemDetail[]
  timeline: OrderTimelineEntry[]
  /** M6a：后端 M6b 才落地，可选以兼容 M4/M5 响应。 */
  evidence?: OrderEvidence
}

/** POST /api/orders/{id}/cancel | /ship 响应 */
export interface StatusResult {
  status: OrderStatus
}

/** GET /api/reports/daily 四方对账 */
export interface DailyReportReconciliation {
  orderAmount: number
  itemAmount: number
  ledgerReserveQty: number
  diff: number
}

/** GET /api/reports/daily 响应 */
export interface DailyReport {
  statDate: string
  orderCnt: number
  paidCnt: number
  cancelCnt: number
  gmv: number
  reconciliation: DailyReportReconciliation
  /** M5 新增：该日日报是否已落 daily_reports 表；可选以兼容 M4 的即时聚合响应。 */
  persisted?: boolean
}

/** GET /api/dead-letters 列表项（沿用 M3 字段） */
export interface DeadLetter {
  id: number
  originalEventId: string
  payload: string
  error: string
  retryCount: number
  status: string
  createdAtUtc: string
  handledAtUtc: string | null
}

/** GET /api/products 商品与库存三态 */
export interface Product {
  id: number
  sku: string
  name: string
  price: number
  available: number
  reserved: number
  deducted: number
  status: string
  /** M6a：乐观锁版本号，后端 M6b 才落地，可选以兼容 M4/M5 响应。 */
  version?: number
}

/** 购物车行 → 下单入参 */
export interface PlaceOrderItem {
  sku: string
  quantity: number
}

/** POST /api/orders 请求体（M1 以来的单 SKU 路径，形状一字不改）。 */
export interface PlaceOrderPayload {
  sku: string
  quantity: number
  idempotencyKey: string
  /**
   * M6a 兼容字段：仅为保持旧类型可赋值而保留。调用方不要在本路径设置 items——
   * 多明细请显式使用 MultiItemOrderPayload（两者同传后端会返回 400）。
   */
  items?: PlaceOrderItem[]
}

/** POST /api/orders 响应 */
export interface PlaceOrderResult {
  id: number
  orderNo: string
}

/** 批量改价请求项 */
export interface PriceUpdate {
  sku: string
  price: number
}

// ---------------------------------------------------------------------------
// M5 契约（字段名 / 类型严格对齐 docs/PLAN.md「M5 任务」DTO 契约表）
// ---------------------------------------------------------------------------

/** 告警级别 */
export type AlertLevel = 'info' | 'warning' | 'critical'

/** GET /api/dashboard 响应 */
export interface DashboardSnapshot {
  generatedAtUtc: string
  today: {
    orderCnt: number
    paidCnt: number
    cancelCnt: number
    gmv: number
  }
  inventory: {
    skuCount: number
    lowStockCount: number
    totalAvailable: number
    lowStockThreshold: number
  }
  queues: {
    fulfillment: number
    orderTimeout: number
    fulfillmentRetry: number
    dlq: number
    available: boolean
  }
  outboxPending: number
  deadLetterPending: number
  alertsActive: number
  controls: {
    consumerEnabled: boolean
    publisherEnabled: boolean
    failureInjectionEnabled: boolean
  }
  source: {
    database: string
    redis: string
    rabbitmq: string
  }
}

/** GET /api/alerts 列表项 */
export interface AlertRecord {
  id: number
  alertKey: string
  level: AlertLevel
  message: string
  createdAtUtc: string
  silenced: boolean
  silencedUntilUtc: string | null
}

/** POST /api/alerts/{key}/silence 响应 */
export interface SilenceResult {
  alertKey: string
  silenced: boolean
  silencedUntilUtc: string | null
}

/** GET /api/lab/state 响应 */
export interface LabState {
  consumerEnabled: boolean
  publisherEnabled: boolean
  failureInjectionEnabled: boolean
  database: string
  redisConfigured: boolean
  rabbitConfigured: boolean
}

/** POST /api/lab/traffic 请求 */
export interface TrafficRequest {
  sku: string
  count: number
  rps: number
  quantity: number
  concurrency?: number
}

/** POST /api/lab/traffic 响应 */
export interface TrafficResult {
  requested: number
  created: number
  failed: number
  elapsedMs: number
  throughputRps: number
  p99Ms: number
  experimentId: number
}

/** POST /api/lab/inject 的 type 取值 */
export type InjectType =
  | 'duplicate'
  | 'failure_on'
  | 'failure_off'
  | 'stop_consumer'
  | 'start_consumer'
  | 'stop_publisher'
  | 'start_publisher'

/** POST /api/lab/inject 请求 */
export interface InjectRequest {
  type: InjectType
  count?: number
  orderNo?: string
}

/** POST /api/lab/inject 响应 */
export interface InjectResult {
  type: string
  applied: boolean
  detail: string
}

/** POST /api/lab/reset 响应 */
export interface ResetResult {
  status: string
  productsSeeded: number
  clearedAtUtc: string
}

/** GET /api/experiments 列表项 / POST /api/experiments/run 响应 */
export interface ExperimentResult {
  id: number
  name: string
  params: string
  result: string
  ranAtUtc: string
  passed: boolean
}

// ---------------------------------------------------------------------------
// M6a 契约（SPEC v0.2，字段名严格对齐 docs/PLAN.md「M6 任务」DTO 契约表）
//
// 兼容性说明：v0.2 的四个新字段在 M6a 一律声明为「可选」——后端要到 M6b
// 才落地，M4/M5 的现有响应不含这些字段，可选类型保证旧响应仍可赋值
// （与 M5 的 DailyReport.persisted 同一处理方式）。M6b 落地后按需收紧。
// ---------------------------------------------------------------------------

/** POST /api/orders 多明细请求体（v0.2 新增，与单 SKU 路径互斥） */
export interface MultiItemOrderPayload {
  items: PlaceOrderItem[]
  idempotencyKey: string
}

/**
 * POST /api/orders 的联合入参：单 SKU 与 items[] 互斥。
 * api.placeOrder 按形状原样构造请求体，绝不把两种字段混进同一个 body。
 */
export type PlaceOrderBody = PlaceOrderPayload | MultiItemOrderPayload

/** GET /api/orders/{id} 可选的链路证据子对象 */
export interface OrderEvidence {
  outboxEventId: string
  outboxStatus: string
  processedEvent: boolean
  ledgerCount: number
  deadLetterCount: number
}

/** GET /api/orders 查询参数（v0.2 新增 orderNo 模糊匹配） */
export interface OrderQuery {
  status?: OrderStatus | ''
  sku?: string
  from?: string
  to?: string
  orderNo?: string
  page?: number
  pageSize?: number
}

// ---------------------------------------------------------------------------
// M6c 契约（GET /api/pipeline，字段名严格对齐 docs/PLAN.md「M6 DTO 契约」表）
// ---------------------------------------------------------------------------

/** 链路泳道单个节点；count 一律为后端真实查询值，不可用时为 -1 且 available=false。 */
export interface PipelineNode {
  key: string
  label: string
  middleware: string
  count: number
  unit: string
  source: string
  available: boolean
  link: string
}

/** GET /api/pipeline 响应 */
export interface PipelineSnapshot {
  generatedAtUtc: string
  nodes: PipelineNode[]
}
