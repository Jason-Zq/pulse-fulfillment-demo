export type StockLevel = 'ok' | 'low' | 'empty'

export interface StockView {
  available: number
  reserved: number
  deducted: number
  total: number
  level: StockLevel
  label: string
  type: 'success' | 'warning' | 'danger'
}

function safeCount(value: number): number {
  return Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0
}

/** 库存三态聚合：保留 available / reserved / deducted 三列，并按可售量给出 el-tag 健康度。 */
export function stockView(
  available: number,
  reserved: number,
  deducted: number,
  lowThreshold = 10,
): StockView {
  const a = safeCount(available)
  const r = safeCount(reserved)
  const d = safeCount(deducted)
  const level: StockLevel = a <= 0 ? 'empty' : a < lowThreshold ? 'low' : 'ok'
  const tones: Record<StockLevel, { label: string; type: StockView['type'] }> = {
    empty: { label: '售罄', type: 'danger' },
    low: { label: '偏低', type: 'warning' },
    ok: { label: '充足', type: 'success' },
  }
  return {
    available: a,
    reserved: r,
    deducted: d,
    total: a + r + d,
    level,
    label: tones[level].label,
    type: tones[level].type,
  }
}
