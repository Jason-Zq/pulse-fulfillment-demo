import type { PipelineNode } from '../api/types'

/** 链路泳道固定顺序：下单事务 → outbox → MQ → Worker → 库存实扣 → 死信 → 日报。 */
export const pipelineOrder = [
  'orders', 'outbox', 'exchange', 'worker', 'deduct', 'deadLetter', 'report',
] as const

/**
 * 节点计数展示。队列不可用（available=false / count=-1）或无节点时显示「—」，
 * 绝不把不可用显示成 0（与 Dashboard 队列深度同一约定）。
 */
export function pipelineCountText(node?: PipelineNode | null): string {
  if (!node || node.available === false || node.count < 0) return '—'
  return String(node.count)
}

/** 按固定顺序排列后端返回的节点；未知 key 不渲染，避免前端擅自补节点。 */
export function orderedPipelineNodes(nodes?: PipelineNode[] | null): PipelineNode[] {
  if (!Array.isArray(nodes)) return []
  const byKey = new Map(nodes.map((n) => [n.key, n]))
  const ordered: PipelineNode[] = []
  for (const key of pipelineOrder) {
    const found = byKey.get(key)
    if (found) ordered.push(found)
  }
  return ordered
}
