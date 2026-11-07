<template>
  <div>
    <div class="snapshot-time">快照时间：{{ formatLocal(snapshot?.generatedAtUtc) }}</div>

    <!-- M6c G2'：一次进入后台就能看到全链路，节点挂真实计数、可点击下钻。 -->
    <ElCard>
      <template #header>
        <div class="card-header">
          <span>订单履约全链路 · 真实计数（{{ pipelineOrder.length }} 个节点，3 秒刷新）</span>
          <RouterLink to="/admin/lab" class="link">去实验台复现 →</RouterLink>
        </div>
      </template>
      <ElEmpty v-if="!pipeline" description="链路数据暂不可用（GET /api/pipeline 未返回，不伪造 0）" :image-size="70" />
      <div v-else class="pipeline-lane">
        <template v-for="(node, i) in orderedNodes" :key="node.key">
          <RouterLink class="pipe-node" :to="node.link">
            <ElTag size="small" :type="middlewareTag(node.middleware).type">{{ node.middleware }}</ElTag>
            <div class="pipe-node__label">{{ node.label }}</div>
            <div class="pipe-node__count">
              {{ pipelineCountText(node) }} <small>{{ node.unit }}</small>
            </div>
            <div class="pipe-node__source">{{ node.source }}</div>
          </RouterLink>
          <span v-if="i < orderedNodes.length - 1" class="pipe-arrow">→</span>
        </template>
      </div>
      <div class="sub">
        不可用节点显示「—」而不是 0；计数来自 GET /api/pipeline 对
        orders / outbox / q.fulfillment / processed_events / inventory_ledgers / dead_letters / daily_reports 的真实查询。
        链路快照 {{ formatLocal(pipeline?.generatedAtUtc) }}
      </div>
    </ElCard>

    <ElCard class="mt">
      <template #header>实测结论（E1 / E2 / E3 最新真实结果）</template>
      <div v-if="!highlights || highlights.length === 0" class="sub">
        暂无实测数据 · 到实验台运行 E1 / E2 / E3 后这里会显示真实数字，不预填示例值
      </div>
      <ElRow v-else :gutter="12">
        <ElCol v-for="h in highlights" :key="h.name" :span="8">
          <div class="evidence-card">
            <div class="sub">{{ h.name }} · {{ h.label }}</div>
            <div class="evidence-value">{{ h.value }}</div>
            <div class="sub">{{ h.caption }}</div>
            <div class="sub">跑于 {{ h.ranAtUtc }}</div>
          </div>
        </ElCol>
      </ElRow>
    </ElCard>

    <ElRow :gutter="16" class="mt">
      <ElCol :span="6">
        <ElCard>
          <template #header>今日订单</template>
          <div class="metric">{{ snapshot?.today.orderCnt ?? '—' }}</div>
          <div class="sub">
            支付 {{ snapshot?.today.paidCnt ?? '—' }} · 取消 {{ snapshot?.today.cancelCnt ?? '—' }}
          </div>
        </ElCard>
      </ElCol>
      <ElCol :span="6">
        <ElCard>
          <template #header>今日 GMV</template>
          <div class="metric">¥ {{ snapshot ? formatAmount(snapshot.today.gmv) : '—' }}</div>
          <div class="sub">今日成交额（元）</div>
        </ElCard>
      </ElCol>
      <ElCol :span="6">
        <ElCard>
          <template #header>库存告警</template>
          <div class="metric">{{ snapshot?.inventory.lowStockCount ?? '—' }}</div>
          <div class="sub">
            SKU {{ snapshot?.inventory.skuCount ?? '—' }} · 可售 {{ snapshot?.inventory.totalAvailable ?? '—' }}
            · 阈值 {{ snapshot?.inventory.lowStockThreshold ?? '—' }}
          </div>
        </ElCard>
      </ElCol>
      <ElCol :span="6">
        <ElCard>
          <template #header>队列深度（4 队列）</template>
          <div v-for="q in queueRows" :key="q.key" class="queue-row">
            <span>{{ q.label }}</span>
            <strong>{{ queueText(q.key) }}</strong>
          </div>
          <div class="sub">
            {{ snapshot?.queues.available === false ? '队列深度不可用（未连接 broker）' : '实时队列深度' }}
          </div>
        </ElCard>
      </ElCol>
    </ElRow>

    <ElCard class="mt">
      <template #header>运行指标（3 秒自动刷新）</template>
      <ElDescriptions :column="3" border>
        <ElDescriptionsItem label="Outbox 待投递">{{ snapshot?.outboxPending ?? '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem label="死信待处理">{{ snapshot?.deadLetterPending ?? '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem label="活跃告警">{{ snapshot?.alertsActive ?? '—' }}</ElDescriptionsItem>
      </ElDescriptions>
    </ElCard>

    <ElCard class="mt">
      <template #header>依赖状态 source</template>
      <ElDescriptions :column="3" border>
        <ElDescriptionsItem label="database">{{ snapshot?.source.database ?? '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem label="redis">{{ snapshot?.source.redis ?? '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem label="rabbitmq">{{ snapshot?.source.rabbitmq ?? '—' }}</ElDescriptionsItem>
      </ElDescriptions>
    </ElCard>

    <ElCard class="mt">
      <template #header>控制开关 controls</template>
      <div v-for="c in controlRows" :key="c.key" class="control-row">
        <span>{{ labControlLabel(c.key).label }}</span>
        <ElTag :type="c.enabled ? 'success' : 'info'">
          {{ c.enabled ? labControlLabel(c.key).onText : labControlLabel(c.key).offText }}
        </ElTag>
      </div>
    </ElCard>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { RouterLink } from 'vue-router'
import {
  ElRow, ElCol, ElCard, ElDescriptions, ElDescriptionsItem, ElTag, ElEmpty, ElMessage,
} from 'element-plus'
import { api } from '../../api/client'
import type { DashboardSnapshot, PipelineSnapshot } from '../../api/types'
import type { EvidenceHighlight } from '../../utils/evidence'
import { parseLatestEvidence } from '../../utils/evidence'
import { pipelineCountText, pipelineOrder, orderedPipelineNodes } from '../../utils/pipeline'
import { usePolling } from '../../composables/usePolling'
import { formatAmount } from '../../utils/metrics'
import { labControlLabel } from '../../utils/alerts'
import type { TagType } from '../../utils/statusTag'

const snapshot = ref<DashboardSnapshot | null>(null)
const pipeline = ref<PipelineSnapshot | null>(null)
const highlights = ref<EvidenceHighlight[] | null>(null)

const orderedNodes = computed(() => orderedPipelineNodes(pipeline.value?.nodes))

type QueueKey = 'fulfillment' | 'orderTimeout' | 'fulfillmentRetry' | 'dlq'

const queueRows: Array<{ key: QueueKey; label: string }> = [
  { key: 'fulfillment', label: '履约 q.fulfillment' },
  { key: 'orderTimeout', label: '超时 q.order.timeout' },
  { key: 'fulfillmentRetry', label: '重试 q.fulfillment.retry' },
  { key: 'dlq', label: '死信 q.dlq' },
]

/** 关键：queues.available === false 时显示「—」，绝不把不可用显示成 0。 */
function queueText(key: QueueKey): string {
  const s = snapshot.value
  if (!s || s.queues.available === false) return '—'
  return String(s.queues[key])
}

const controlRows = computed(() => {
  const c = snapshot.value?.controls
  return [
    { key: 'consumerEnabled', enabled: c?.consumerEnabled ?? false },
    { key: 'publisherEnabled', enabled: c?.publisherEnabled ?? false },
    { key: 'failureInjectionEnabled', enabled: c?.failureInjectionEnabled ?? false },
  ]
})

function middlewareTag(name: string): { type: TagType } {
  if (name === 'MySQL') return { type: 'primary' }
  if (name === 'Redis') return { type: 'warning' }
  if (name === 'RabbitMQ') return { type: 'success' }
  return { type: 'info' }
}

function formatLocal(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString()
}

async function load(): Promise<void> {
  try {
    snapshot.value = await api.getDashboard()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '仪表盘加载失败')
  }

  // 泳道与实验证据是独立数据源：失败时保留上一次结果 / 显示「暂无实测数据」，不伪造计数。
  try {
    pipeline.value = await api.getPipeline()
  } catch {
    pipeline.value = null
  }
  try {
    highlights.value = parseLatestEvidence(await api.listExperiments())
  } catch {
    highlights.value = null
  }
}

// usePolling 内部 start() + refresh()，并在 onUnmounted / onScopeDispose 自动清理定时器。
usePolling(load)
</script>

<style scoped>
.snapshot-time { margin-bottom: 12px; color: #909399; font-size: 13px; }
.card-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.link { color: #409eff; text-decoration: none; font-size: 13px; }
.pipeline-lane { display: flex; flex-wrap: wrap; align-items: stretch; gap: 6px; }
.pipe-node {
  flex: 1 1 150px; min-width: 150px;
  display: flex; flex-direction: column; gap: 6px;
  padding: 12px; border: 1px solid #e6ebf3; border-radius: 10px;
  background: #fbfcff; text-decoration: none; color: #1f2937;
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}
.pipe-node:hover { transform: translateY(-2px); box-shadow: 0 8px 20px rgba(31,50,90,0.10); }
.pipe-node__label { font-size: 14px; font-weight: 600; }
.pipe-node__count { font-size: 24px; font-weight: 700; color: #3b5bdb; line-height: 1.2; }
.pipe-node__count small { font-size: 12px; font-weight: 400; color: #909399; }
.pipe-node__source { font-size: 11px; color: #94a3b8; font-family: ui-monospace, Menlo, monospace; line-height: 1.5; }
.pipe-arrow { align-self: center; color: #94a3b8; font-size: 16px; }
.pipe-empty { padding: 16px; border: 1px dashed #e6ebf3; border-radius: 8px; color: #909399; font-size: 13px; }
.evidence-card { border: 1px solid #e6ebf3; border-radius: 10px; padding: 12px 14px; background: #fbfcff; }
.evidence-value { margin: 6px 0; font-size: 22px; font-weight: 700; color: #2f49b0; }
.metric { font-size: 30px; font-weight: 600; line-height: 1.3; }
.sub { margin-top: 8px; color: #909399; font-size: 12px; line-height: 1.7; }
.queue-row, .control-row {
  display: flex; align-items: center; justify-content: space-between;
  padding: 4px 0; font-size: 13px;
}
.mt { margin-top: 16px; }
</style>
