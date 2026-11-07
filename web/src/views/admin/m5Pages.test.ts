import { describe, it, expect } from 'vitest'
// ?raw 把 .vue 当纯文本载入，可在 node 测试环境对 SFC 做静态契约断言。
import dashboardSource from './Dashboard.vue?raw'
import alertsSource from './Alerts.vue?raw'
import labSource from './Lab.vue?raw'

function mustContain(source: string, tokens: string[]): void {
  for (const token of tokens) expect(source).toContain(token)
}

describe('M5 页面静态契约 — 三个后台页面具备关键行为', () => {
  it('Dashboard.vue：4 张指标卡 + usePolling 3 秒 + 队列不可用显示「—」', () => {
    mustContain(dashboardSource, [
      'ElRow', 'ElCol', 'ElCard', 'usePolling', 'getDashboard',
      'queues.available', '—', 'generatedAtUtc', 'controls', 'source',
    ])
  })

  it('Alerts.vue：级别标签 + 静默对话框 minutes + UTC→本地时间 + 写后刷新', () => {
    mustContain(alertsSource, [
      'ElTable', 'ElDialog', 'alertLevelTag', 'silenceAlert', 'minutes',
      'toLocaleString', 'listAlerts',
    ])
  })

  it('Lab.vue：演示声明 + 7 个注入开关 + E1/E2/E3 + 结果容错 + 二次确认 + 不预填假数据', () => {
    mustContain(labSource, [
      'DEMO_BANNER', 'parseExperimentJson', 'formatExperimentResult',
      'ElMessageBox.confirm', 'runExperiment', 'listExperiments', 'resetLab',
      'runTraffic', 'inject', 'throughputRps', 'p99Ms', '—',
      'duplicate', 'failure_on', 'failure_off', 'stop_consumer',
      'start_consumer', 'stop_publisher', 'start_publisher',
      "'E1'", "'E2'", "'E3'",
      'ref<TrafficResult | null>(null)',
    ])
  })
})
