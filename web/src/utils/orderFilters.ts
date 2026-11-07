export interface OrderFilter {
  status?: string
  sku?: string
  /** M6b：GET /api/orders 新增的订单号模糊匹配。 */
  orderNo?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 20

function positiveInt(value: number | undefined, fallback: number): number {
  return value !== undefined && Number.isFinite(value) && value > 0 ? Math.floor(value) : fallback
}

/** 筛选条件 → query string：裁剪空值、保留日期范围、附带分页（参数顺序稳定，便于契约测试）。 */
export function buildOrderQuery(filter: OrderFilter): string {
  const params = new URLSearchParams()
  if (filter.status) params.set('status', filter.status)
  if (filter.sku) params.set('sku', filter.sku)
  if (filter.orderNo) params.set('orderNo', filter.orderNo)
  if (filter.from) params.set('from', filter.from)
  if (filter.to) params.set('to', filter.to)
  params.set('page', String(positiveInt(filter.page, DEFAULT_PAGE)))
  params.set('pageSize', String(positiveInt(filter.pageSize, DEFAULT_PAGE_SIZE)))
  return params.toString()
}
