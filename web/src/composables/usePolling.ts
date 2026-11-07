import { getCurrentInstance, getCurrentScope, onScopeDispose, onUnmounted } from 'vue'

export interface PollingControls {
  start: () => void
  stop: () => void
  /** 写操作后立即重新拉取一次（不重置 3 秒计时器）。 */
  refresh: () => void
  readonly active: boolean
}

/**
 * 3 秒轮询 + 写操作后立即刷新。不引入 SignalR / WebSocket / 浏览器直连 MQ。
 * 组件 setup 内用 onUnmounted 清定时器；effectScope dispose 让组合式作用域与单测也能清理。
 */
export function usePolling(task: () => void | Promise<void>, intervalMs = 3000): PollingControls {
  let timer: ReturnType<typeof setInterval> | null = null
  let running = false

  const stop = (): void => {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
    running = false
  }

  const start = (): void => {
    if (timer !== null) return
    running = true
    timer = setInterval(() => {
      void task()
    }, intervalMs)
  }

  const refresh = (): void => {
    void task()
  }

  if (getCurrentInstance()) onUnmounted(stop)
  if (getCurrentScope()) onScopeDispose(stop)

  start()
  refresh()

  return {
    start,
    stop,
    refresh,
    get active() {
      return running
    },
  }
}
