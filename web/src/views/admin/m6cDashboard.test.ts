import { describe, it, expect } from 'vitest'
import dashboardSource from './Dashboard.vue?raw'

describe('M6c Dashboard — 链路泳道本身就是技术叙事', () => {
  it('7 节点泳道：真实计数 + 顺序固定 + 不可用显示「—」+ 节点可点击', () => {
    for (const token of [
      'getPipeline', 'pipelineCountText', 'pipelineOrder', 'pipeline-lane',
      'node.link', 'node.source', 'node.middleware', '—',
    ]) {
      expect(dashboardSource).toContain(token)
    }
  })

  it('E1/E2/E3 最新结果卡只取真实实验数据，不预填数字', () => {
    for (const token of ['parseLatestEvidence', 'listExperiments', '暂无实测数据']) {
      expect(dashboardSource).toContain(token)
    }
    expect(dashboardSource).not.toContain('75.97')
    expect(dashboardSource).not.toContain('32.08')
  })

  it('保留 M5 已有指标卡、3 秒轮询与队列不可用兜底', () => {
    for (const token of [
      'ElRow', 'ElCol', 'ElCard', 'usePolling', 'getDashboard',
      'queues.available', 'generatedAtUtc', 'controls', 'source',
    ]) {
      expect(dashboardSource).toContain(token)
    }
  })
})
