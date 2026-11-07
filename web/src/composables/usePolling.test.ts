import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { effectScope } from 'vue'
import { usePolling } from './usePolling'

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('usePolling — 3 秒轮询 + 写后立即刷新 + 卸载清理', () => {
  it('创建后立即拉取一次', () => {
    const task = vi.fn()
    const scope = effectScope()
    scope.run(() => usePolling(task))
    expect(task).toHaveBeenCalledTimes(1)
    scope.stop()
  })

  it('默认每 3000ms 轮询一次', () => {
    const task = vi.fn()
    const scope = effectScope()
    scope.run(() => usePolling(task))
    vi.advanceTimersByTime(3000)
    expect(task).toHaveBeenCalledTimes(2)
    vi.advanceTimersByTime(6000)
    expect(task).toHaveBeenCalledTimes(4)
    scope.stop()
  })

  it('refresh() 写操作后立即拉取，且不额外创建定时器', () => {
    const task = vi.fn()
    const scope = effectScope()
    const polling = scope.run(() => usePolling(task))!
    expect(task).toHaveBeenCalledTimes(1)
    polling.refresh()
    expect(task).toHaveBeenCalledTimes(2)
    vi.advanceTimersByTime(3000)
    expect(task).toHaveBeenCalledTimes(3)
    scope.stop()
  })

  it('stop() 后不再轮询', () => {
    const task = vi.fn()
    const scope = effectScope()
    const polling = scope.run(() => usePolling(task))!
    polling.stop()
    vi.advanceTimersByTime(9000)
    expect(task).toHaveBeenCalledTimes(1)
    scope.stop()
  })

  it('scope.stop() 清理定时器（等价于组件 onUnmounted）', () => {
    const task = vi.fn()
    const scope = effectScope()
    scope.run(() => usePolling(task))
    scope.stop()
    vi.advanceTimersByTime(9000)
    expect(task).toHaveBeenCalledTimes(1)
  })

  it('支持自定义间隔', () => {
    const task = vi.fn()
    const scope = effectScope()
    scope.run(() => usePolling(task, 1000))
    vi.advanceTimersByTime(1000)
    expect(task).toHaveBeenCalledTimes(2)
    scope.stop()
  })

  it('active 反映运行状态', () => {
    const task = vi.fn()
    const scope = effectScope()
    const polling = scope.run(() => usePolling(task))!
    expect(polling.active).toBe(true)
    polling.stop()
    expect(polling.active).toBe(false)
    scope.stop()
  })
})
