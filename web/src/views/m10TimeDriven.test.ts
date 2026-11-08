import { describe, it, expect } from 'vitest'
import detailSource from './OrderDetail.vue?raw'

describe('M10 订单关单倒计时静态契约', () => {
  it('倒计时复用 ttl 纯函数，按 1 秒真实时钟刷新并清理定时器', () => {
    for (const token of [
      'countdownText',
      'expiresAtUtc',
      'Date.now()',
      'setInterval',
      'clearInterval',
      'onUnmounted',
      '1000',
      '距自动关单',
    ]) {
      expect(detailSource).toContain(token)
    }
  })

  it('截止时间只来自后端 expiresAtUtc，页面不自己写死 15 分钟 / 900000 ms', () => {
    expect(detailSource).toContain("order.status === 'Created'")
    for (const forbidden of ['15 * 60', '900000', '900_000', '900 * 1000']) {
      expect(detailSource).not.toContain(forbidden)
    }
  })

  it('不引入长连接或新依赖（3 秒轮询 + 本地 1 秒倒计时即可）', () => {
    expect(detailSource).not.toContain('@microsoft/signalr')
    expect(detailSource).not.toContain('new WebSocket')
    expect(detailSource).not.toContain("from 'axios'")
  })
})
