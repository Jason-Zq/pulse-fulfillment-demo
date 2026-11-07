import type { AlertLevel } from '../api/types'
import type { StatusTag } from './statusTag'

const ALERT_LEVEL_MAP: Record<AlertLevel, StatusTag> = {
  info: { label: '提示', type: 'info' },
  warning: { label: '警告', type: 'warning' },
  critical: { label: '严重', type: 'danger' },
}

/** 告警级别 → 中文标签 + el-tag type；未知级别安全回退。 */
export function alertLevelTag(level: string): StatusTag {
  return ALERT_LEVEL_MAP[level as AlertLevel] ?? { label: level, type: 'info' }
}

export interface LabControlLabel {
  label: string
  onText: string
  offText: string
}

const LAB_CONTROL_MAP: Record<string, LabControlLabel | undefined> = {
  consumerEnabled: { label: '消费者', onText: '运行中', offText: '已停止' },
  publisherEnabled: { label: 'Publisher', onText: '运行中', offText: '已停止' },
  failureInjectionEnabled: { label: '故障注入', onText: '已开启', offText: '已关闭' },
}

/** 运行态开关名 → 中文名与开 / 关文案；未知开关安全回退。 */
export function labControlLabel(key: string): LabControlLabel {
  return LAB_CONTROL_MAP[key] ?? { label: key, onText: '开', offText: '关' }
}
