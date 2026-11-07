import type { PlaceOrderBody, PlaceOrderItem } from '../api/types'

/** POST /api/orders 请求体：单 SKU 或 items[] 多明细，二选一。 */
export type PreparedOrder = PlaceOrderBody

/**
 * 构造下单请求（SPEC v0.2）：
 * - 购物车 1 行 → 保持 M1 单 SKU 形状 `{ sku, quantity, idempotencyKey }`，
 *   `scripts/verify_docker_e2e.py` 的 132 条容器断言与 E1/E2/E3 都走这条路径；
 * - 购物车多行 → `{ items, idempotencyKey }`，后端同事务写多行明细 / 预扣 / 流水。
 *
 * 幂等键来自 crypto.randomUUID()：一次结算会话一个键，重复点击 / 网络重试都复用它，
 * 后端据 idempotency_key 唯一索引返回同一订单。
 */
export function buildPlaceOrder(items: PlaceOrderItem[]): PreparedOrder {
  const idempotencyKey = crypto.randomUUID()
  if (items.length > 1) {
    return {
      items: items.map((line) => ({ sku: line.sku, quantity: line.quantity })),
      idempotencyKey,
    }
  }

  const first = items[0]
  return {
    sku: first?.sku ?? '',
    quantity: first?.quantity ?? 0,
    idempotencyKey,
  }
}
