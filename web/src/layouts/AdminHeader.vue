<template>
  <div class="admin-header">
    <div class="deps">
      <span>MySQL <strong>{{ dep('database') }}</strong></span>
      <span>Redis <strong>{{ dep('redis') }}</strong></span>
      <span>RabbitMQ <strong>{{ dep('rabbitmq') }}</strong></span>
    </div>
    <span class="demo">{{ DEMO_BANNER }}</span>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { api } from '../api/client'
import type { DashboardSnapshot } from '../api/types'
import { usePolling } from '../composables/usePolling'
import { DEMO_BANNER } from '../router/routes'

const snapshot = ref<DashboardSnapshot | null>(null)
type DepKey = 'database' | 'redis' | 'rabbitmq'

/** 依赖不可用时不伪造状态：无快照 / 空值一律显示「—」。 */
function dep(key: DepKey): string {
  const value = snapshot.value?.source[key]
  return value && value.length > 0 ? value : '—'
}

async function load(): Promise<void> {
  try {
    snapshot.value = await api.getDashboard()
  } catch {
    // 保留上一次快照；拉取失败时由 dep() 兜底为「—」，不假装依赖健康。
  }
}

// 3 秒轮询 + 首屏立即刷新，与后台其它页面同一节奏（不引入 SignalR）。
usePolling(load)
</script>

<style scoped>
.admin-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 0 12px;
  background: #fff;
  font-size: 12px;
}
.deps { display: flex; gap: 20px; color: #606266; }
.deps strong { color: #303133; font-weight: 500; }
.demo { color: #909399; }
</style>
