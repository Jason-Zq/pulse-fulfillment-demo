import { describe, it, expect } from 'vitest'
import { alertLevelTag, labControlLabel } from './alerts'

describe('alertLevelTag — info/warning/critical → 中文标签 + el-tag type', () => {
  const cases: Array<[string, string, string]> = [
    ['info', '提示', 'info'],
    ['warning', '警告', 'warning'],
    ['critical', '严重', 'danger'],
  ]

  for (const [level, label, type] of cases) {
    it(`${level} → ${label} / ${type}`, () => {
      expect(alertLevelTag(level)).toEqual({ label, type })
    })
  }

  it('未知级别安全回退为原文 + info', () => {
    expect(alertLevelTag('unknown')).toEqual({ label: 'unknown', type: 'info' })
  })

  it('空字符串也安全回退', () => {
    expect(alertLevelTag('')).toEqual({ label: '', type: 'info' })
  })
})

describe('labControlLabel — 开关名 → 中文名 + on/off 文案', () => {
  it('consumerEnabled → 消费者 / 运行中 / 已停止', () => {
    expect(labControlLabel('consumerEnabled')).toEqual({
      label: '消费者', onText: '运行中', offText: '已停止',
    })
  })

  it('publisherEnabled → Publisher / 运行中 / 已停止', () => {
    expect(labControlLabel('publisherEnabled')).toEqual({
      label: 'Publisher', onText: '运行中', offText: '已停止',
    })
  })

  it('failureInjectionEnabled → 故障注入 / 已开启 / 已关闭', () => {
    expect(labControlLabel('failureInjectionEnabled')).toEqual({
      label: '故障注入', onText: '已开启', offText: '已关闭',
    })
  })

  it('未知开关名安全回退', () => {
    expect(labControlLabel('whatever')).toEqual({
      label: 'whatever', onText: '开', offText: '关',
    })
  })
})
