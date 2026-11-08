/**
 * M10：订单关单倒计时的纯函数。
 *
 * 后端从 MySQL 读回的 DateTime.Kind = Unspecified，GET /api/orders/{id} 真响应是
 *   '2026-11-08T03:41:56.527641'（不带 Z）。
 * 浏览器 Date.parse 会把无时区字符串当**本地时间**，在 Asia/Shanghai 会凭空多算 8 小时，
 * 所以这里一律按 UTC 解释：没有时区标记时补 'Z'。
 * 截止时间只来自后端 expiresAtUtc，前端不自己算 15 分钟，也不参与关单。
 */

const ZONE_SUFFIX = /(?:Z|[+-]\d{2}:?\d{2})$/i

/** 把后端 UTC 时间串解析成 epoch 毫秒；空值 / 非法值返回 null（不伪造 0）。 */
export function parseUtc(value: string): number | null {
  const raw = (value ?? '').trim()
  if (!raw) return null
  const normalized = ZONE_SUFFIX.test(raw) ? raw : `${raw.replace(' ', 'T')}Z`
  const ms = Date.parse(normalized)
  return Number.isFinite(ms) ? ms : null
}

/** 剩余秒数（向下取整）；负数表示已到期；截止时间不可用返回 null。 */
export function remainingSeconds(expiresAtUtc: string, nowMs: number): number | null {
  const deadline = parseUtc(expiresAtUtc)
  if (deadline === null) return null
  return Math.floor((deadline - nowMs) / 1000)
}

/** 展示用 mm:ss；已到期明确说明在等关单；数据不可用显示「—」。 */
export function countdownText(expiresAtUtc: string, nowMs: number): string {
  const seconds = remainingSeconds(expiresAtUtc, nowMs)
  if (seconds === null) return '—'
  if (seconds <= 0) return '已到期，等待关单'
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60
  return `${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`
}
