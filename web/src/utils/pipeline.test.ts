import { describe, it, expect } from 'vitest'
import { pipelineCountText, pipelineOrder } from './pipeline'
import type { PipelineNode } from '../api/types'

function node(over: Partial<PipelineNode> = {}): PipelineNode {
  return {
    key: 'orders', label: '下单事务', middleware: 'MySQL', count: 3,
    unit: '笔订单', source: 'orders', available: true, link: '/admin/orders',
    ...over,
  }
}

describe('M6c 链路泳道纯函数 — 不可用绝不显示成 0', () => {
  it('可用节点返回真实计数字符串', () => {
    expect(pipelineCountText(node({ count: 7 }))).toBe('7')
    expect(pipelineCountText(node({ count: 0 }))).toBe('0')
  })

  it('available=false / count=-1 / 空值一律显示「—」', () => {
    expect(pipelineCountText(node({ available: false, count: 0 }))).toBe('—')
    expect(pipelineCountText(node({ count: -1 }))).toBe('—')
    expect(pipelineCountText(null)).toBe('—')
    expect(pipelineCountText(undefined)).toBe('—')
  })

  it('链路顺序固定为 7 个真实节点', () => {
    expect(pipelineOrder).toEqual([
      'orders', 'outbox', 'exchange', 'worker', 'deduct', 'deadLetter', 'report',
    ])
  })
})
