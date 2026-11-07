import { describe, it, expect } from 'vitest'
import { parseLatestEvidence } from './evidence'
import type { ExperimentResult } from '../api/types'

function exp(
  name: string,
  result: unknown,
  passed = true,
  ranAtUtc = '2026-11-07T00:00:00Z',
  id = 1,
): ExperimentResult {
  return {
    id,
    name,
    params: '{}',
    result: JSON.stringify(result),
    ranAtUtc,
    passed,
  }
}

describe('parseLatestEvidence — 首页证据数字只取真实实验结果', () => {
  it('无结果 / null / 非数组 → null（首页显示「暂无实测数据」，不预填）', () => {
    expect(parseLatestEvidence(null)).toBeNull()
    expect(parseLatestEvidence(undefined)).toBeNull()
    expect(parseLatestEvidence([])).toBeNull()
  })

  it('只认 E1 / E2 / E3，忽略其它实验名', () => {
    expect(parseLatestEvidence([exp('E4', { foo: 1 })])).toBeNull()
  })

  it('passed=false 或 result 非法 JSON 一律不展示，避免把失败数据当证据', () => {
    expect(parseLatestEvidence([
      exp('E2', { duplicateCount: 1000, deductCount: 2 }, false),
    ])).toBeNull()
    const broken = exp('E2', { duplicateCount: 1000, deductCount: 1 })
    broken.result = '{not json'
    expect(parseLatestEvidence([broken])).toBeNull()
  })

  it('同一实验取最新一条（ranAtUtc 降序，平手按 id 降序）', () => {
    const older = exp(
      'E3', { requested: 10, oversoldCount: 1, throughputRps: 1, p99Ms: 2 },
      true, '2026-11-06T00:00:00Z', 1,
    )
    const newer = exp(
      'E3', { requested: 100, oversoldCount: 0, throughputRps: 75.97, p99Ms: 32.08 },
      true, '2026-11-07T00:00:00Z', 2,
    )
    const out = parseLatestEvidence([newer, older])
    expect(out).toHaveLength(1)
    expect(out?.[0].caption).toContain('75.97 RPS')
  })

  it('E1 / E2 / E3 各提取真实数字，按 E1→E2→E3 顺序返回', () => {
    const rows: ExperimentResult[] = [
      exp('E2', {
        duplicateCount: 1000, confirmed: 1000, deductCount: 1, processedRows: 1, passed: true,
      }, true, '2026-11-07T02:00:00Z', 2),
      exp('E3', {
        requested: 100, created: 11, failed: 89, throughputRps: 75.97, p99Ms: 32.08,
        oversoldCount: 0, passed: true,
      }, true, '2026-11-07T03:00:00Z', 3),
      exp('E1', {
        orders: 20, placed: 20, pendingBefore: 20, pendingAfter: 0,
        published: 20, diff: 0, passed: true,
      }, true, '2026-11-07T01:00:00Z', 1),
    ]
    const out = parseLatestEvidence(rows)
    expect(out?.map((x) => x.name)).toEqual(['E1', 'E2', 'E3'])
    expect(out?.[0].value).toBe('20 条已补投')
    expect(out?.[1].value).toBe('1 次实扣')
    expect(out?.[2].value).toBe('超卖 0')
    expect(out?.[2].caption).toContain('75.97 RPS')
    expect(out?.[2].caption).toContain('32.08 ms')
    expect(out?.every((x) => x.passed)).toBe(true)
  })

  it('缺少关键数字字段的记录跳过，不编造 0', () => {
    expect(parseLatestEvidence([exp('E2', { duplicateCount: 1000 })])).toBeNull()
    expect(parseLatestEvidence([exp('E1', { orders: 20 })])).toBeNull()
    expect(parseLatestEvidence([exp('E3', { oversoldCount: 0 })])).toBeNull()
  })
})
