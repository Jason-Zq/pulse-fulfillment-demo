<template>
  <div class="shop-page">
    <div class="shop-section__head">
      <div>
        <h1 class="shop-section__title">我的订单</h1>
        <div class="shop-section__hint">列表每 3 秒自动刷新（轮询，不使用 WebSocket / 浏览器直连 MQ）</div>
      </div>
      <RouterLink to="/"><ElButton text>继续选购 →</ElButton></RouterLink>
    </div>

    <div v-if="pendingCount > 0" class="orders-pending">
      <span>当前页有 <strong>{{ pendingCount }}</strong> 笔订单待模拟支付，15 分钟未支付会由 TTL + DLX 自动关单。</span>
      <RouterLink :to="`/order/${firstPendingId}`"><ElButton size="small" type="primary">去支付</ElButton></RouterLink>
    </div>

    <div class="shop-panel">
      <div class="shop-toolbar">
        <ElInput v-model="filters.orderNo" clearable placeholder="按订单号搜索" style="width: 220px" @keyup.enter="onSearch" />
        <ElSelect v-model="filters.status" style="width: 160px">
          <ElOption label="全部状态" value="" />
          <ElOption v-for="s in statusOptions" :key="s" :label="statusTag(s).label" :value="s" />
        </ElSelect>
        <ElButton type="primary" @click="onSearch">查询</ElButton>
        <ElButton @click="onReset">重置</ElButton>
      </div>
    </div>

    <div class="shop-panel">
      <ElAlert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <ElEmpty v-else-if="orders.length === 0" description="还没有订单，去商城下一单试试" />
      <template v-else>
        <ElTable :data="orders" border>
          <ElTableColumn prop="orderNo" label="订单号" width="190" />
          <ElTableColumn prop="sku" label="SKU" width="130" />
          <ElTableColumn label="状态" width="110">
            <template #default="{ row }">
              <ElTag :type="statusTag(row.status).type">{{ statusTag(row.status).label }}</ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="金额" width="120">
            <template #default="{ row }">¥ {{ Number(row.totalAmount).toFixed(2) }}</template>
          </ElTableColumn>
          <ElTableColumn prop="createdAtUtc" label="下单时间（UTC）" />
          <ElTableColumn label="操作" width="220">
            <template #default="{ row }">
              <div class="shop-row">
                <RouterLink v-if="row.status === 'Created'" :to="`/order/${row.id}`">
                  <ElButton size="small" type="primary">去支付</ElButton>
                </RouterLink>
                <RouterLink v-else :to="`/order/${row.id}`"><ElButton size="small">详情</ElButton></RouterLink>
                <ElButton size="small" :disabled="row.status !== 'Created'" @click="cancelOne(row.id)">取消</ElButton>
              </div>
            </template>
          </ElTableColumn>
        </ElTable>
        <div style="display: flex; justify-content: flex-end; margin-top: 16px">
          <ElPagination
            v-model:current-page="filters.page"
            :total="total"
            :page-size="filters.pageSize"
            layout="total, prev, pager, next"
            @current-change="load"
          />
        </div>
      </template>
    </div>

    <TechEvidenceCard
      :middlewares="['MySQL', 'RabbitMQ']"
      :keys="['orders', 'order_items', 'outbox', 'q.order.timeout']"
      :reproduce="{ label: '去后台看全量订单', path: '/admin/orders' }"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { RouterLink } from 'vue-router'
import {
  ElButton, ElInput, ElSelect, ElOption, ElTable, ElTableColumn,
  ElTag, ElAlert, ElEmpty, ElPagination, ElMessage,
} from 'element-plus'
import { api } from '../api/client'
import type { OrderListItem, OrderStatus } from '../api/types'
import { buildOrderQuery } from '../utils/orderFilters'
import { statusTag } from '../utils/statusTag'
import { usePolling } from '../composables/usePolling'
import TechEvidenceCard from '../components/TechEvidenceCard.vue'

const statusOptions: OrderStatus[] = [
  'Created', 'Paid', 'Shipped', 'Completed', 'Cancelled', 'Refunding', 'Refunded',
]

const filters = reactive({ orderNo: '', status: '', page: 1, pageSize: 10 })
const orders = ref<OrderListItem[]>([])
const total = ref(0)
const error = ref('')

const pendingOrders = computed(() => orders.value.filter((o) => o.status === 'Created'))
const pendingCount = computed(() => pendingOrders.value.length)
const firstPendingId = computed(() => pendingOrders.value[0]?.id ?? '')

async function load(): Promise<void> {
  try {
    const data = await api.listOrders(buildOrderQuery(filters))
    orders.value = data.items
    total.value = data.total
    error.value = ''
  } catch (e) {
    error.value = e instanceof Error ? e.message : '订单加载失败（请先启动后端 API）'
  }
}

function onSearch(): void {
  filters.page = 1
  polling.refresh()
}

function onReset(): void {
  filters.orderNo = ''
  filters.status = ''
  filters.page = 1
  polling.refresh()
}

const polling = usePolling(load, 3000)

async function cancelOne(id: number): Promise<void> {
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
.orders-pending {
  display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px;
  margin-bottom: 16px; padding: 12px 18px; border-radius: 10px;
  background: #fff8ec; border: 1px solid #f6d9a8;
  font-size: 13px; color: #92400e;
}
.orders-pending strong { font-size: 16px; }
</style>
