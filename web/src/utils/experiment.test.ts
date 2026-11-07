import { describe, it, expect } from 'vitest'
import { parseExperimentJson, formatExperimentResult } from './experiment'

describe('parseExperimentJson — 容错解析实验 params / result（JSON 字符串，不抛异常）', () => {
  it('合法 JSON 对象解析出对象', () => {
    expect(parseExperimentJson('{"requested":20,"p99Ms":12.5}')).toEqual({ requested: 20, p99Ms: 12.5 })
  })

  it('JSON 数组 / 数字 / 字符串 / 布尔也能解析', () => {
    expect(parseExperimentJson('[1,2,3]')).toEqual([1, 2, 3])
    expect(parseExperimentJson('42')).toBe(42)
    expect(parseExperimentJson('"ok"')).toBe('ok')
    expect(parseExperimentJson('true')).toBe(true)
  })

  it('非法 JSON 不抛异常并返回 null', () => {
    expect(() => parseExperimentJson('{not json')).not.toThrow()
    expect(parseExperimentJson('{not json')).toBeNull()
    expect(parseExperimentJson('undefined')).toBeNull()
  })

  it('空串 / 空白 / null / undefined 返回 null', () => {
    expect(parseExperimentJson('')).toBeNull()
    expect(parseExperimentJson('   ')).toBeNull()
    expect(parseExperimentJson(null)).toBeNull()
    expect(parseExperimentJson(undefined)).toBeNull()
  })
})

describe('formatExperimentResult — 实验结果摘要', () => {
  it('非法 / 空 JSON 返回占位 —', () => {
    expect(formatExperimentResult('broken')).toBe('—')
    expect(formatExperimentResult(null)).toBe('—')
    expect(formatExperimentResult('')).toBe('—')
  })

  it('挑出关键指标拼成 key=value 摘要，忽略无关字段', () => {
    const text = formatExperimentResult(
      '{"passed":true,"throughputRps":12.5,"p99Ms":88,"diff":0,"note":"ignored"}',
    )
    expect(text).toContain('passed=true')
    expect(text).toContain('throughputRps=12.5')
    expect(text).toContain('p99Ms=88')
    expect(text).toContain('diff=0')
    expect(text).not.toContain('note')
  })

  it('无已知键时退化为紧凑 JSON 文本', () => {
    expect(formatExperimentResult('{"foo":1}')).toBe('{"foo":1}')
  })

  it('基础类型直接转字符串', () => {
    expect(formatExperimentResult('42')).toBe('42')
    expect(formatExperimentResult('"ok"')).toBe('ok')
  })

  it('超长文本按 maxLength 截断并加省略号', () => {
    const text = formatExperimentResult(JSON.stringify({ foo: 'x'.repeat(300) }), 40)
    expect(text.length).toBeLessThanOrEqual(41)
    expect(text.endsWith('…')).toBe(true)
  })
})
