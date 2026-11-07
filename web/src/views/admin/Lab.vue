<template>
  <div>
    <ElAlert :title="DEMO_BANNER" type="warning" :closable="false" show-icon />

    <ElCard class="mt">
      <template #header>流量生成器（结果均为接口真实返回，未运行时显示「—」）</template>
      <ElForm :inline="true" :model="trafficForm">
        <ElFormItem label="SKU">
          <ElInput v-model="trafficForm.sku" placeholder="SKU" style="width: 150px" />
        </ElFormItem>
        <ElFormItem label="订单数 count">
          <ElInputNumber v-model="trafficForm.count" :min="1" :max="10000" />
        </ElFormItem>
        <ElFormItem label="目标 RPS">
          <ElInputNumber v-model="trafficForm.rps" :min="1" :max="1000" />
        </ElFormItem>
        <ElFormItem label="每件数量 quantity">
          <ElInputNumber v-model="trafficForm.quantity" :min="1" />
        </ElFormItem>
        <ElFormItem label="并发 concurrency">
          <ElInputNumber v-model="trafficForm.concurrency" :min="1" :max="64" />
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" :loading="trafficRunning" @click="submitTraffic">生成流量</ElButton>
        </ElFormItem>
      </ElForm>
      <ElDescriptions :column="4" border class="mt">
        <ElDescriptionsItem label="请求数 requested">{{ metric(trafficResult?.requested) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="成功 created">{{ metric(trafficResult?.created) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="失败 failed">{{ metric(trafficResult?.failed) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="耗时 elapsedMs">{{ metric(trafficResult?.elapsedMs) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="实测吞吐 throughputRps">{{ metric(trafficResult?.throughputRps) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="实测 P99 p99Ms">{{ metric(trafficResult?.p99Ms) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="实验 ID experimentId">{{ metric(trafficResult?.experimentId) }}</ElDescriptionsItem>
      </ElDescriptions>
    </ElCard>

    <ElCard class="mt">
      <template #header>故障注入（7 个开关）</template>
      <div class="toolbar">
        <ElButton
          v-for="item in injectButtons"
          :key="item.value"
          :loading="injecting === item.value"
          @click="runInject(item.value)"
        >
          {{ item.label }}
        </ElButton>
      </div>
      <p class="hint">最近一次注入：{{ lastInject || '—' }}</p>
    </ElCard>

    <ElCard class="mt">
      <template #header>实验 E1 / E2 / E3 与演示数据</template>
      <div class="toolbar">
        <ElButton
          v-for="name in experimentNames"
          :key="name"
          type="primary"
          :loading="runningExperiment === name"
          @click="runExperiment(name)"
        >
          运行 {{ name }}
        </ElButton>
        <ElButton type="danger" :loading="resetting" @click="resetDemo">一键清空演示数据</ElButton>
      </div>
      <ElTable :data="experiments" border class="mt">
        <ElTableColumn prop="id" label="ID" width="80" />
        <ElTableColumn prop="name" label="实验" width="100" />
        <ElTableColumn label="参数 params">
          <template #default="{ row }"><code>{{ displayJson(row.params) }}</code></template>
        </ElTableColumn>
        <ElTableColumn label="结果 result（摘要）">
          <template #default="{ row }"><code>{{ formatExperimentResult(row.result) }}</code></template>
        </ElTableColumn>
        <ElTableColumn label="结论" width="100">
          <template #default="{ row }">
            <ElTag :type="row.passed ? 'success' : 'danger'">{{ row.passed ? '通过' : '未通过' }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="ranAtUtc" label="运行时间（UTC）" width="200" />
        <template #empty>
          <ElEmpty description="暂无实测数据 · 运行 E1 / E2 / E3 后结果会自动出现在这里" :image-size="70" />
        </template>
      </ElTable>
    </ElCard>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import {
  ElAlert, ElCard, ElForm, ElFormItem, ElInput, ElInputNumber, ElButton,
  ElDescriptions, ElDescriptionsItem, ElTable, ElTableColumn, ElTag, ElEmpty,
  ElMessage, ElMessageBox,
} from 'element-plus'
import { api } from '../../api/client'
import type {
  ExperimentResult, InjectRequest, InjectType, TrafficRequest, TrafficResult,
} from '../../api/types'
import { DEMO_BANNER } from '../../router/routes'
import { parseExperimentJson, formatExperimentResult } from '../../utils/experiment'
import { usePolling } from '../../composables/usePolling'

// 表单默认值只是请求参数；结果区一律以接口真实返回填充，未运行时显示占位「—」。
const trafficForm = reactive({
  sku: '', count: 20, rps: 5, quantity: 1, concurrency: 1,
})
const trafficResult = ref<TrafficResult | null>(null)
const trafficRunning = ref(false)

const injectButtons: Array<{ label: string; value: InjectType }> = [
  { label: '重复消息 duplicate', value: 'duplicate' },
  { label: '开启故障注入 failure_on', value: 'failure_on' },
  { label: '关闭故障注入 failure_off', value: 'failure_off' },
  { label: '停消费者 stop_consumer', value: 'stop_consumer' },
  { label: '启消费者 start_consumer', value: 'start_consumer' },
  { label: '停 Publisher stop_publisher', value: 'stop_publisher' },
  { label: '启 Publisher start_publisher', value: 'start_publisher' },
]
const injecting = ref<InjectType | ''>('')
const lastInject = ref('')

const experimentNames = ['E1', 'E2', 'E3'] as const
const runningExperiment = ref('')
const experiments = ref<ExperimentResult[]>([])
const resetting = ref(false)

function metric(value?: number | null): string {
  return value === undefined || value === null ? '—' : String(value)
}

/** params / result 是 JSON 字符串：容错解析后美化展示，非法内容原样显示不抛异常。 */
function displayJson(raw: string): string {
  const parsed = parseExperimentJson(raw)
  if (parsed === null) return raw || '—'
  return JSON.stringify(parsed, null, 2)
}

async function loadExperiments(): Promise<void> {
  try {
    experiments.value = await api.listExperiments()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '实验结果加载失败')
  }
}

// 3 秒轮询：实验由后台异步执行，结果表自动刷新；写操作仍立即 loadExperiments()。
const polling = usePolling(loadExperiments)

async function submitTraffic(): Promise<void> {
  trafficRunning.value = true
  try {
    const payload: TrafficRequest = {
      sku: trafficForm.sku,
      count: trafficForm.count,
      rps: trafficForm.rps,
      quantity: trafficForm.quantity,
      concurrency: trafficForm.concurrency,
    }
    trafficResult.value = await api.runTraffic(payload)
    ElMessage.success('流量生成完成，数值为接口真实返回')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '流量生成失败')
  } finally {
    trafficRunning.value = false
  }
}

async function runInject(type: InjectType): Promise<void> {
  injecting.value = type
  try {
    const payload: InjectRequest = type === 'duplicate' ? { type, count: 1 } : { type }
    const result = await api.inject(payload)
    lastInject.value = `${result.type} · applied=${result.applied} · ${result.detail}`
    ElMessage.success(result.applied ? '注入已应用' : '注入未应用')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '注入失败')
  } finally {
    injecting.value = ''
  }
}

async function runExperiment(name: (typeof experimentNames)[number]): Promise<void> {
  runningExperiment.value = name
  try {
    const result = await api.runExperiment(name)
    ElMessage.success(`实验 ${name}：${result.passed ? '通过' : '未通过'}`)
    await loadExperiments()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : `实验 ${name} 运行失败`)
  } finally {
    runningExperiment.value = ''
  }
}

async function resetDemo(): Promise<void> {
  try {
    await ElMessageBox.confirm(
      '将清空业务数据并重新播种演示 SKU（保留控制开关与实验结果），确认继续？',
      '清空演示数据',
      { type: 'warning', confirmButtonText: '确认清空', cancelButtonText: '取消' },
    )
  } catch {
    return // 用户取消
  }
  resetting.value = true
  try {
    const result = await api.resetLab()
    ElMessage.success(`已清空：status=${result.status}，播种 ${result.productsSeeded} 个 SKU`)
    trafficResult.value = null
    polling.refresh() // 写操作后立即刷新实验结果表
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '清空失败')
  } finally {
    resetting.value = false
  }
}
</script>

<style scoped>
.mt {
  margin-top: 16px;
}
.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.hint {
  margin: 12px 0 0;
  color: #909399;
  font-size: 13px;
}
code {
  white-space: pre-wrap;
  word-break: break-all;
  font-size: 12px;
}
</style>
