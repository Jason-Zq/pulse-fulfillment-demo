/** 实验 params / result 是后端落库的 JSON 字符串，可能为空或非法；解析失败一律不抛异常。 */
export function parseExperimentJson(raw?: string | null): unknown {
  if (raw === null || raw === undefined || raw.trim() === '') return null
  try {
    return JSON.parse(raw) as unknown
  } catch {
    return null
  }
}

const SUMMARY_KEYS = [
  'passed', 'throughputRps', 'p99Ms', 'diff', 'summary', 'message', 'detail',
] as const

function formatScalar(value: unknown): string {
  if (typeof value === 'object' && value !== null) return JSON.stringify(value)
  return String(value)
}

/** 把实验 result 的 JSON 字符串压成一行摘要；非法 / 空返回占位「—」。 */
export function formatExperimentResult(raw?: string | null, maxLength = 160): string {
  const parsed = parseExperimentJson(raw)
  if (parsed === null) return '—'
  if (typeof parsed !== 'object') return String(parsed)

  const obj = parsed as Record<string, unknown>
  const picked = SUMMARY_KEYS
    .filter((key) => obj[key] !== undefined)
    .map((key) => `${key}=${formatScalar(obj[key])}`)
  const text = picked.length > 0 ? picked.join(' · ') : JSON.stringify(parsed)

  return text.length > maxLength ? `${text.slice(0, maxLength)}…` : text
}
