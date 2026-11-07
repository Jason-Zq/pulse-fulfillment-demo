import type { DailyReport } from '../api/types'

/** 指标窗口裁剪：保留序列最近 windowSize 个点。 */
export function clipWindow<T>(series: readonly T[], windowSize: number): T[] {
  if (windowSize <= 0) return []
  return series.slice(Math.max(0, series.length - windowSize))
}

/** 日报数值对齐：把后端可能残缺的响应补成完整 DailyReport，避免页面出现 NaN。 */
export function alignDailyReport(raw?: Partial<DailyReport> | null): DailyReport {
  const rec = raw?.reconciliation
  const orderAmount = rec?.orderAmount ?? 0
  const itemAmount = rec?.itemAmount ?? 0
  const ledgerReserveQty = rec?.ledgerReserveQty ?? 0
  return {
    statDate: raw?.statDate ?? '',
    orderCnt: raw?.orderCnt ?? 0,
    paidCnt: raw?.paidCnt ?? 0,
    cancelCnt: raw?.cancelCnt ?? 0,
    gmv: raw?.gmv ?? 0,
    reconciliation: {
      orderAmount,
      itemAmount,
      ledgerReserveQty,
      diff: rec?.diff ?? orderAmount - itemAmount,
    },
  }
}

/** 金额展示：空值按 0 处理，固定两位小数。 */
export function formatAmount(value?: number | null): string {
  return (value ?? 0).toFixed(2)
}
