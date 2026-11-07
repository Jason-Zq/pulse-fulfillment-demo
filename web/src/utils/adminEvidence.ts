/** 后台每一页的「本页技术证据」唯一真源；AdminLayout 按路由取元数据后统一渲染。 */

export interface AdminPageMeta {
  title: string
  breadcrumb: string[]
  description: string
  middlewares: string[]
  keys: string[]
  reproduce?: { label: string; path: string }
}

export const ADMIN_PAGE_META: Record<string, AdminPageMeta> = {
  '/admin/dashboard': {
    title: '仪表盘',
    breadcrumb: ['运营后台', '仪表盘'],
    description: '链路泳道 7 个节点挂 GET /api/pipeline 的真实计数；队列不可用显示「—」不伪造 0。',
    middlewares: ['MySQL', 'RabbitMQ', 'Redis'],
    keys: ['orders', 'outbox', 'ex.orders', 'q.fulfillment', 'processed_events', 'inventory_ledgers', 'daily_reports'],
    reproduce: { label: '去实验台复现 E1/E2/E3', path: '/admin/lab' },
  },
  '/admin/alerts': {
    title: '告警中心',
    breadcrumb: ['运营后台', '告警中心'],
    description: '死信 / 低库存 / 队列积压扫描；Redis 短钥匙做去重与静默 TTL。',
    middlewares: ['Redis', 'MySQL', 'RabbitMQ'],
    keys: ['alert:seen:{key}', 'alert:silence:{key}', 'dead_letters', 'q.dlq.fulfillment'],
    reproduce: { label: '去实验台注入故障', path: '/admin/lab' },
  },
  '/admin/lab': {
    title: '实验台',
    breadcrumb: ['运营后台', '实验台'],
    description: 'E1 消息不丢 / E2 消费幂等 / E3 并发不超卖，结果落 experiment_results，绝不预填数字。',
    middlewares: ['MySQL', 'RabbitMQ', 'Redis'],
    keys: ['experiment_results', 'lab_controls', 'ex.orders', 'dedup:{event_id}'],
    reproduce: { label: '回到链路总览看计数', path: '/admin/dashboard' },
  },
  '/admin/orders': {
    title: '订单列表',
    breadcrumb: ['运营后台', '订单列表'],
    description: '订单、明细与 outbox 同事务写入；取消会释放预扣，15 分钟 TTL + DLX 自动关单。',
    middlewares: ['MySQL', 'RabbitMQ'],
    keys: ['orders', 'order_items', 'outbox', 'q.order.timeout'],
    reproduce: { label: '去链路总览', path: '/admin/dashboard' },
  },
  '/admin/products': {
    title: '商品与库存',
    breadcrumb: ['运营后台', '商品与库存'],
    description: '库存三态与乐观锁版本号；并发拿到旧 version 会被拒绝，因此不超卖。',
    middlewares: ['MySQL'],
    keys: ['products', 'inventories', 'Inventory.Version', 'inventory_ledgers'],
    reproduce: { label: '跑 E3 并发不超卖', path: '/admin/lab' },
  },
  '/admin/fulfillment': {
    title: '发货与售后',
    breadcrumb: ['运营后台', '发货与售后'],
    description: 'Worker 按 order_items 逐行实扣并写 Deduct 流水；售后流转仍是前端演示，未接后端持久化。',
    middlewares: ['MySQL', 'RabbitMQ'],
    keys: ['orders', 'inventory_ledgers', 'q.fulfillment', 'processed_events'],
    reproduce: { label: '去死信补偿台', path: '/admin/dead-letters' },
  },
  '/admin/reports': {
    title: '对账报表',
    breadcrumb: ['运营后台', '对账报表'],
    description: '四方对账即时聚合；Quartz 每天 01:00 落 daily_reports，按 stat_date 幂等 upsert。',
    middlewares: ['MySQL'],
    keys: ['daily_reports', 'orders', 'order_items', 'inventory_ledgers'],
    reproduce: { label: '去实验台造流量', path: '/admin/lab' },
  },
  '/admin/dead-letters': {
    title: '死信补偿台',
    breadcrumb: ['运营后台', '死信补偿台'],
    description: '三次重试后进 DLQ；人工补偿用新 event_id 写 pending outbox 重投。',
    middlewares: ['RabbitMQ', 'MySQL', 'Redis'],
    keys: ['q.dlq.fulfillment', 'dead_letters', 'processed_events', 'outbox'],
    reproduce: { label: '去实验台注入失败消息', path: '/admin/lab' },
  },
}

const FALLBACK: AdminPageMeta = {
  title: '运营后台',
  breadcrumb: ['运营后台'],
  description: '演示环境 · 全部数据为模拟 · 不涉及真实交易。',
  middlewares: ['MySQL'],
  keys: ['orders'],
}

/** 未知后台路径安全回退，保证面包屑与证据卡不会空白。 */
export function adminPageMeta(path: string): AdminPageMeta {
  return ADMIN_PAGE_META[path] ?? FALLBACK
}
