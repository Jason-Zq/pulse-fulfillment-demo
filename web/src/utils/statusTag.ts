import type { OrderStatus } from '../api/types'

export type TagType = 'primary' | 'success' | 'info' | 'warning' | 'danger'

export interface StatusTag {
  label: string
  type: TagType
}

const STATUS_MAP: Record<OrderStatus, StatusTag> = {
  Created: { label: '待支付', type: 'info' },
  Paid: { label: '待发货', type: 'primary' },
  Shipped: { label: '已发货', type: 'primary' },
  Completed: { label: '已完成', type: 'success' },
  Cancelled: { label: '已取消', type: 'info' },
  Refunding: { label: '退款中', type: 'warning' },
  Refunded: { label: '已退款', type: 'danger' },
}

/** 订单状态 → 中文标签 + el-tag type；未知状态安全回退。 */
export function statusTag(status: string): StatusTag {
  return STATUS_MAP[status as OrderStatus] ?? { label: status, type: 'info' }
}
