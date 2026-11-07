<template>
  <div>
    <ElCard>
      <template #header>死信补偿台（DLQ 人工补偿）</template>
      <ElForm :inline="true">
        <ElFormItem label="状态">
          <ElSelect v-model="status" clearable placeholder="全部" style="width: 170px">
            <ElOption label="待处理 pending" value="pending" />
            <ElOption label="已处理 handled" value="handled" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" @click="load">查询</ElButton>
        </ElFormItem>
      </ElForm>
      <ElTable :data="deadLetters" border class="mt">
        <ElTableColumn prop="originalEventId" label="原始事件 ID" width="220" />
        <ElTableColumn prop="error" label="错误" />
        <ElTableColumn prop="retryCount" label="重试次数" width="110" />
        <ElTableColumn label="状态" width="130">
          <template #default="{ row }">
            <ElTag :type="row.status === 'handled' ? 'success' : 'warning'">{{ row.status }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="createdAtUtc" label="发生时间（UTC）" width="210" />
        <ElTableColumn label="操作" width="140">
          <template #default="{ row }">
            <ElButton
              size="small"
              type="primary"
              :disabled="row.status === 'handled'"
              @click="retry(row.id)"
            >
              一键重试
            </ElButton>
          </template>
        </ElTableColumn>
        <template #empty>
          <ElEmpty description="暂无死信 · 可在实验台注入失败消息触发三次重试" :image-size="70" />
        </template>
      </ElTable>
    </ElCard>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import {
  ElCard,
  ElForm,
  ElFormItem,
  ElSelect,
  ElOption,
  ElButton,
  ElTable,
  ElTableColumn,
  ElTag,
  ElEmpty,
  ElMessage,
} from 'element-plus'
import { api } from '../../api/client'
import type { DeadLetter } from '../../api/types'
import { usePolling } from '../../composables/usePolling'

const status = ref('')
const deadLetters = ref<DeadLetter[]>([])

async function load(): Promise<void> {
  try {
    deadLetters.value = await api.listDeadLetters(status.value || undefined)
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '死信列表加载失败')
  }
}

// 3 秒轮询：死信由 Worker 异步产生；人工补偿后立即 refresh()。
const polling = usePolling(load)

async function retry(id: string): Promise<void> {
  try {
    await api.retryDeadLetter(id)
    ElMessage.success('已触发人工补偿重试')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '重试失败')
  } finally {
    // 写操作后立即重新拉取
    polling.refresh()
  }
}
</script>

<style scoped>
.mt {
  margin-top: 16px;
}
</style>
