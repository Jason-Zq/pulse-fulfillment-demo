import { describe, it, expect } from 'vitest'
import aboutSource from './About.vue?raw'

describe('M6a 关于本项目 — 证据数字来自真实实验结果', () => {
  it('About.vue 接 parseLatestEvidence + listExperiments，无数据显示「暂无实测数据」', () => {
    for (const token of ['parseLatestEvidence', 'listExperiments', '暂无实测数据']) {
      expect(aboutSource).toContain(token)
    }
  })

  it('About.vue 不预填写死的吞吐 / P99 数字', () => {
    expect(aboutSource).not.toContain('75.97')
    expect(aboutSource).not.toContain('32.08')
  })

  it('M6b：三中间件卡片只描述实验设计，不写死样本量与结果数字', () => {
    for (const token of ['20 单', '1000 条', '超卖率 0']) {
      expect(aboutSource).not.toContain(token)
    }
  })

  it('M6b：About 页套用商城视觉底座，不重复渲染演示声明', () => {
    expect(aboutSource).toContain('shop-page')
    expect(aboutSource).not.toContain('ElAlert')
  })
})
