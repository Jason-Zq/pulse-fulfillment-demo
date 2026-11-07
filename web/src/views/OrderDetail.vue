<template>
  <div class="shop-page">
    <div class="shop-section__head">
      <div>
        <h1 class="shop-section__title">订单详情</h1>
        <div class="shop-section__hint">状态每 3 秒自动刷新 · 异步履约由 Worker 消费 RabbitMQ 消息完成</div>
      </div>
      <RouterLink to="/orders"><ElButton text>← 我的订单</ElButton></RouterLink>
    </div>

    <ElAlert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div v-else-if="!order" class="shop-empty">正在读取订单 …</div>

    <template v-else>
      <div class="shop-panel">
        <div class="shop-row">
          <span class="shop-mono">{{ order.orderNo }}</span>
          <ElTag :type="statusTag(order.status).type">{{ statusTag(order.status).label }}</ElTag>
          <div class="shop-spacer"></div>
          <ElButton size="small" @click="polling.refresh()">刷新状态</ElButton>
        </div>
        <div class="shop-detail-kv">
          <div><span class="shop-kv__k">订单金额</span><div class="shop-total"><strong>¥ {{ Number(order.totalAmount).toFixed(2) }}</strong></div></div>
          <div><span class="shop-kv__k">下单时间（UTC）</span><div class="shop-kv__v shop-mono">{{ order.createdAtUtc }}</div></div>
          <div><span class="shop-kv__k">支付时间（UTC）</span><div class="shop-kv__v shop-mono">{{ order.paidAtUtc ?? '—' }}</div></div>
          <div><span class="shop-kv__k">关单截止（UTC）</span><div class="shop-kv__v shop-mono">{{ order.expiresAtUtc }}</div></div>
        </div>

        <div class="shop-action-bar">
          <ElButton
            type="primary"
            :disabled="order.status !== 'Created'"
            :loading="paying"
            @click="pay"
          >
            模拟支付 · 演示
          </ElButton>
          <ElButton :disabled="order.status !== 'Created'" @click="cancel">取消订单</ElButton>
          <span class="shop-note">
            {{ order.status === 'Created'
              ? '点击后会在同一事务写入 order.paid 消息，由 Worker 异步完成库存实扣；不涉及任何真实收款。'
              : '当前状态不可再发起模拟支付。支付后的状态变化由 3 秒轮询呈现。' }}
          </span>
        </div>
      </div>

      <div class="shop-panel">
        <h2 class="shop-panel__title">商品明细（{{ order.items.length }} 行）</h2>
        <ElTable :data="order.items" border>
          <ElTableColumn prop="sku" label="SKU" width="180" />
          <ElTableColumn prop="quantity" label="数量" width="120" />
          <ElTableColumn label="单价" width="140">
            <template #default="{ row }">¥ {{ Number(row.unitPrice).toFixed(2) }}</template>
          </ElTableColumn>
          <ElTableColumn label="小计">
            <template #default="{ row }">¥ {{ (Number(row.unitPrice) * Number(row.quantity)).toFixed(2) }}</template>
          </ElTableColumn>
        </ElTable>
      </div>

      <div class="shop-panel">
        <h2 class="shop-panel__title">状态时间线</h2>
        <ElTimeline>
          <ElTimelineItem
            v-for="(t, i) in order.timeline"
            :key="i"
            :timestamp="t.atUtc"
            :type="statusTag(t.status).type"
          >
            {{ statusTag(t.status).label }} · {{ t.note }}
          </ElTimelineItem>
        </ElTimeline>
      </div>

      <div class="shop-panel">
        <h2 class="shop-panel__title">这笔订单的链路证据</h2>
        <div v-if="order.evidence" class="shop-kv">
          <div class="shop-kv__item"><span class="shop-kv__k">outbox 事件</span><span class="shop-kv__v shop-mono">{{ order.evidence.outboxEventId }}</span></div>
          <div class="shop-kv__item"><span class="shop-kv__k">outbox 状态</span><span class="shop-kv__v">{{ order.evidence.outboxStatus }}</span></div>
          <div class="shop-kv__item"><span class="shop-kv__k">消费判重命中</span><span class="shop-kv__v">{{ order.evidence.processedEvent ? '是' : '否' }}</span></div>
          <div class="shop-kv__item"><span class="shop-kv__k">库存流水行数</span><span class="shop-kv__v">{{ order.evidence.ledgerCount }}</span></div>
          <div class="shop-kv__item"><span class="shop-kv__k">死信条数</span><span class="shop-kv__v">{{ order.evidence.deadLetterCount }}</span></div>
        </div>
        <div v-else class="shop-note">
          后端尚未随详情返回单笔链路计数（SPEC v0.2 允许延后到 M6c）。此处不伪造 0：
          全局真实计数请看运维后台的仪表盘与实验台。
        </div>
      </div>

      <TechEvidenceCard
        :middlewares="['MySQL', 'RabbitMQ', 'Redis']"
        :keys="['orders', 'outbox', 'ex.orders', 'q.fulfillment', 'dedup:{event_id}', 'inventory_ledgers']"
        :reproduce="{ label: '去后台看链路总览', path: '/admin/dashboard' }"
      />
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, RouterLink } from 'vue-router'
import {
  ElButton, ElTag, ElAlert, ElTable, ElTableColumn,
  ElTimeline, ElTimelineItem, ElMessage,
} from 'element-plus'
import { api } from '../api/client'
import type { OrderDetail } from '../api/types'
import { statusTag } from '../utils/statusTag'
import { usePolling } from '../composables/usePolling'
import TechEvidenceCard from '../components/TechEvidenceCard.vue'

const route = useRoute()
const id = String(route.params.id ?? '')
const order = ref<OrderDetail | null>(null)
const error = ref('')
const paying = ref(false)

async function load(): Promise<void> {
  try {
    order.value = await api.getOrder(id)
    error.value = ''
  } catch {
    // 轮询失败保留上一次结果，不打断页面；首次加载失败才提示。
    if (!order.value) error.value = '订单加载失败（请先启动后端 API，或确认订单号正确）'
  }
}

// 3 秒轮询：浏览器不连 MQ / WebSocket；写操作（pay / cancel）后立即 refresh()。
const polling = usePolling(load, 3000)

async function pay(): Promise<void> {
  paying.value = true
  try {
    await api.payOrder(id)
    ElMessage.success('已发起模拟支付，等待 Worker 异步履约')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '模拟支付失败')
  } finally {
    paying.value = false
    polling.refresh()
  }
}

async function cancel(): Promise<void> {
  try {
    await api.cancelOrder(id)
    ElMessage.success('订单已取消，预扣库存已释放')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '取消失败')
  } finally {
    polling.refresh()
  }
}
</script>

<style scoped>
.shop-detail-kv {
  display: grid; grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px; margin-top: 18px;
}
.shop-action-bar {
  display: flex; flex-wrap: wrap; align-items: center; gap: 12px;
  margin-top: 20px; padding-top: 18px; border-top: 1px solid var(--shop-line);
}
.shop-action-bar .shop-note { flex: 1; min-width: 220px; }
@media (max-width: 860px) { .shop-detail-kv { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
