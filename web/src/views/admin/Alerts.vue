<template>
  <div>
    <ElCard>
      <template #header>告警中心（写操作后立即刷新）</template>
      <ElTable :data="alerts" border>
        <ElTableColumn prop="id" label="ID" width="80" />
        <ElTableColumn prop="alertKey" label="告警键" width="180" />
        <ElTableColumn label="级别" width="110">
          <template #default="{ row }">
            <ElTag :type="alertLevelTag(row.level).type">{{ alertLevelTag(row.level).label }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="message" label="内容" />
        <ElTableColumn label="静默状态" width="120">
          <template #default="{ row }">
            <ElTag :type="row.silenced ? 'success' : 'info'">
              {{ row.silenced ? '已静默' : '未静默' }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="静默截止（本地）" width="190">
          <template #default="{ row }">{{ formatLocal(row.silencedUntilUtc) }}</template>
        </ElTableColumn>
        <ElTableColumn label="发生时间（本地）" width="190">
          <template #default="{ row }">{{ formatLocal(row.createdAtUtc) }}</template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="120">
          <template #default="{ row }">
            <ElButton size="small" type="primary" :disabled="row.silenced" @click="openSilence(row.alertKey)">
              静默
            </ElButton>
          </template>
        </ElTableColumn>
        <template #empty>
          <ElEmpty description="暂无告警 · 死信 / 低库存 / 队列积压扫描结果会出现在这里" :image-size="70" />
        </template>
      </ElTable>
    </ElCard>

    <ElDialog v-model="dialogVisible" title="静默告警" width="430px">
      <ElForm :model="silenceForm">
        <ElFormItem label="告警键">
          <span>{{ currentKey || '—' }}</span>
        </ElFormItem>
        <ElFormItem label="静默时长（分钟，1–1440）">
          <ElInputNumber v-model="silenceForm.minutes" :min="1" :max="1440" :step="5" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="dialogVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="saving" @click="applySilence">确认静默</ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import {
  ElCard, ElTable, ElTableColumn, ElTag, ElButton, ElDialog, ElForm, ElFormItem,
  ElInputNumber, ElEmpty, ElMessage,
} from 'element-plus'
import { api } from '../../api/client'
import type { AlertRecord } from '../../api/types'
import { alertLevelTag } from '../../utils/alerts'
import { usePolling } from '../../composables/usePolling'

const alerts = ref<AlertRecord[]>([])
const dialogVisible = ref(false)
const currentKey = ref('')
const saving = ref(false)
const silenceForm = reactive({ minutes: 30 })

/** UTC ISO 字符串 → 浏览器本地时间展示；空值显示占位「—」。 */
function formatLocal(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString()
}

async function load(): Promise<void> {
  try {
    alerts.value = await api.listAlerts()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '告警列表加载失败')
  }
}

// 3 秒轮询：告警由 Worker 异步产生；静默写操作后仍立即 load() 再刷新一次。
const polling = usePolling(load)

function openSilence(alertKey: string): void {
  currentKey.value = alertKey
  silenceForm.minutes = 30
  dialogVisible.value = true
}

async function applySilence(): Promise<void> {
  saving.value = true
  try {
    await api.silenceAlert(currentKey.value, silenceForm.minutes)
    ElMessage.success('告警已静默')
    dialogVisible.value = false
    polling.refresh() // 写操作后立即刷新列表
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '静默失败')
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
</style>
