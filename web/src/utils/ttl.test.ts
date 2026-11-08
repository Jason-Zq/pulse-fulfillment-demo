import { describe, it, expect } from 'vitest'
import { parseUtc, remainingSeconds, countdownText } from './ttl'

// 后端从 MySQL 读回的 DateTime.Kind=Unspecified，真响应不带 Z：
// '2026-11-08T03:41:56.527641'。浏览器 Date.parse 会把它当本地时间，
// UTC+8 环境会凭空多算 8 小时，所以纯函数必须显式按 UTC 解释。
const EXPIRES = '2026-11-08T03:41:56.527641'
const DEADLINE = Date.parse('2026-11-08T03:41:56.527Z')

describe('parseUtc', () => {
  it('无时区标记的后端时间按 UTC 解释，而不是浏览器本地时间', () => {
    expect(parseUtc(EXPIRES)).toBe(DEADLINE)
  })

  it('MySQL 空格分隔格式同样按 UTC 解释', () => {
    expect(parseUtc('2026-11-08 03:41:56.527641')).toBe(DEADLINE)
  })

  it('带 Z 的 ISO 串原样解析', () => {
    expect(parseUtc('2026-11-08T03:41:56.527Z')).toBe(DEADLINE)
  })

  it('带显式时区偏移时尊重该偏移', () => {
    const withZone = '2026-11-08T11:41:56+08:00'
    expect(parseUtc(withZone)).toBe(Date.parse(withZone))
  })

  it('空串 / 非法串返回 null，不伪造 0', () => {
    expect(parseUtc('')).toBeNull()
    expect(parseUtc('not-a-date')).toBeNull()
  })
})

describe('remainingSeconds', () => {
  it('剩余秒数向下取整', () => {
    expect(remainingSeconds(EXPIRES, DEADLINE - 61_500)).toBe(61)
    expect(remainingSeconds(EXPIRES, DEADLINE - 1_999)).toBe(1)
  })

  it('恰好到期为 0，超过到期返回负数', () => {
    expect(remainingSeconds(EXPIRES, DEADLINE)).toBe(0)
    expect(remainingSeconds(EXPIRES, DEADLINE + 3_000)).toBe(-3)
  })

  it('非法截止时间返回 null', () => {
    expect(remainingSeconds('bad', DEADLINE)).toBeNull()
  })
})

describe('countdownText', () => {
  it('正常展示 mm:ss', () => {
    expect(countdownText(EXPIRES, DEADLINE - 899_000)).toBe('14:59')
    expect(countdownText(EXPIRES, DEADLINE - 61_500)).toBe('01:01')
    expect(countdownText(EXPIRES, DEADLINE - 1_999)).toBe('00:01')
  })

  it('到期后不显示负时间，明确说明在等关单', () => {
    expect(countdownText(EXPIRES, DEADLINE)).toBe('已到期，等待关单')
    expect(countdownText(EXPIRES, DEADLINE + 60_000)).toBe('已到期，等待关单')
  })

  it('数据不可用显示「—」，不伪造 0', () => {
    expect(countdownText('bad', DEADLINE)).toBe('—')
    expect(countdownText('', DEADLINE)).toBe('—')
  })
})
