import type { ExperimentResult } from '../api/types'
import { parseExperimentJson } from './experiment'

/** 首页 / 关于页展示的一条真实实验证据；所有数字均来自 experiment_results 的 result 字段。 */
export interface EvidenceHighlight {
  name: 'E1' | 'E2' | 'E3'
  label: string
  value: string
  caption: string
  passed: boolean
  ranAtUtc: string
}

const ORDER = ['E1', 'E2', 'E3'] as const
type ExperimentName = (typeof ORDER)[number]

const LABELS: Record<ExperimentName, string> = {
  E1: '消息不丢',
  E2: '消费幂等',
  E3: '并发不超卖',
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function numeric(obj: Record<string, unknown>, key: string): number | null {
  const value = obj[key]
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

/** 去掉多余的末尾 0：20.00 → 20，75.97 → 75.97，0.00 → 0。 */
function fmt(value: number): string {
  return value.toFixed(2).replace(/\.?0+$/, '')
}

function build(row: ExperimentResult, parsed: Record<string, unknown>): EvidenceHighlight | null {
  const base = { passed: row.passed, ranAtUtc: row.ranAtUtc }

  switch (row.name) {
    case 'E1': {
      const published = numeric(parsed, 'published')
      const orders = numeric(parsed, 'orders')
      const pendingAfter = numeric(parsed, 'pendingAfter')
      const diff = numeric(parsed, 'diff')
      if (published === null || orders === null || pendingAfter === null || diff === null) return null
      return {
        ...base,
        name: 'E1',
        label: LABELS.E1,
        value: `${fmt(published)} 条已补投`,
        caption: `停 Publisher 期间 ${fmt(orders)} 单 · 待投递 ${fmt(pendingAfter)} · 四方对账差异 ${fmt(diff)}`,
      }
    }
    case 'E2': {
      const duplicateCount = numeric(parsed, 'duplicateCount')
      const deductCount = numeric(parsed, 'deductCount')
      if (duplicateCount === null || deductCount === null) return null
      return {
        ...base,
        name: 'E2',
        label: LABELS.E2,
        value: `${fmt(deductCount)} 次实扣`,
        caption: `${fmt(duplicateCount)} 条重复投递 · 库存只扣 ${fmt(deductCount)} 次`,
      }
    }
    case 'E3': {
      const requested = numeric(parsed, 'requested')
      const oversoldCount = numeric(parsed, 'oversoldCount')
      const throughputRps = numeric(parsed, 'throughputRps')
      const p99Ms = numeric(parsed, 'p99Ms')
      if (requested === null || oversoldCount === null || throughputRps === null || p99Ms === null) return null
      return {
        ...base,
        name: 'E3',
        label: LABELS.E3,
        value: `超卖 ${fmt(oversoldCount)}`,
        caption: `${fmt(requested)} 单并发 · ${fmt(throughputRps)} RPS · P99 ${fmt(p99Ms)} ms`,
      }
    }
    default:
      return null
  }
}

/**
 * 从 GET /api/experiments 的结果里挑出 E1/E2/E3 各自最新一条真实证据。
 *
 * 硬规则（对应 SPEC v0.2 G1'）：只展示 passed=true 且 result 可解析、关键数字齐全的记录；
 * 字段缺失 / 非法 JSON / 失败实验一律跳过，绝不预填示例数字；一条都没有时返回 null，
 * 由页面显示「暂无实测数据」。
 */
export function parseLatestEvidence(results?: ExperimentResult[] | null): EvidenceHighlight[] | null {
  if (!Array.isArray(results) || results.length === 0) return null

  const out: EvidenceHighlight[] = []
  for (const name of ORDER) {
    const candidates = results
      .filter((row) => row.name === name && row.passed)
      .slice()
      .sort((a, b) => {
        const byTime = b.ranAtUtc.localeCompare(a.ranAtUtc)
        return byTime !== 0 ? byTime : b.id - a.id
      })

    for (const row of candidates) {
      const parsed = parseExperimentJson(row.result)
      if (!isRecord(parsed)) continue
      const highlight = build(row, parsed)
      if (highlight !== null) {
        out.push(highlight)
        break
      }
    }
  }

  return out.length > 0 ? out : null
}
