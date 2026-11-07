<template>
  <div>
    <ElCard>
      <template #header>发货工作台（Paid → Shipped，列表 3 秒自动刷新）</template>
      <ElTable :data="pending" border>
        <ElTableColumn prop="orderNo" label="订单号" width="200" />
        <ElTableColumn prop="sku" label="SKU" width="140" />
        <ElTableColumn label="金额" width="130">
          <template #default="{ row }">¥ {{ Number(row.totalAmount).toFixed(2) }}</template>
        </ElTableColumn>
        <ElTableColumn prop="createdAtUtc" label="下单时间（UTC）" />
        <ElTableColumn label="操作" width="120">
          <template #default="{ row }">
            <ElButton size="small" type="primary" @click="ship(row.id)">发货</ElButton>
          </template>
        </ElTableColumn>
        <template #empty>
          <ElEmpty description="暂无待发货订单 · 模拟支付后 Worker 会异步完成履约" :image-size="70" />
        </template>
      </ElTable>
    </ElCard>

    <ElCard class="mt">
      <template #header>售后简化流转（前端演示，不持久化）</template>
      <ElSteps :active="afterSaleStep" finish-status="success" align-center>
        <ElStep title="已发货 Shipped" />
        <ElStep title="退款中 Refunding" />
        <ElStep title="已退款 Refunded" />
      </ElSteps>
      <div class="actions">
        <ElButton :disabled="afterSaleStep >= 2" @click="advanceAfterSale">推进售后状态</ElButton>
        <ElButton @click="afterSaleStep = 0">重置演示</ElButton>
      </div>
      <ElAlert
        title="售后状态为前端演示流转，未接后端持久化；状态机与退款流程见 SPEC 第 5 节，属 M4 范围外。"
        type="info"
        :closable="false"
      />
    </ElCard>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import {
  ElCard,
  ElTable,
  ElTableColumn,
  ElButton,
  ElSteps,
  ElStep,
  ElAlert,
  ElEmpty,
  ElMessage,
} from 'element-plus'
import { api } from '../../api/client'
import type { OrderListItem } from '../../api/types'
import { buildOrderQuery } from '../../utils/orderFilters'
import { usePolling } from '../../composables/usePolling'

const pending = ref<OrderListItem[]>([])
const afterSaleStep = ref(0)

async function load(): Promise<void> {
  try {
    const data = await api.listOrders(buildOrderQuery({ status: 'Paid', pageSize: 50 }))
    pending.value = data.items
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '待发货订单加载失败')
  }
}

// 3 秒轮询；发货写操作后立即刷新。
const polling = usePolling(load)

async function ship(id: string): Promise<void> {
  try {
    await api.shipOrder(id)
    ElMessage.success('已发货')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '发货失败')
  } finally {
    polling.refresh()
  }
}

function advanceAfterSale(): void {
  afterSaleStep.value = Math.min(afterSaleStep.value + 1, 2)
}
</script>

<style scoped>
.mt {
  margin-top: 16px;
}
.actions {
  display: flex;
  gap: 12px;
  margin: 16px 0;
}
</style>
