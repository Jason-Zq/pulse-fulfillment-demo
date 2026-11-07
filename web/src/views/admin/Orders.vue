<template>
  <div>
    <ElCard>
      <template #header>订单筛选（列表 3 秒自动刷新）</template>
      <ElForm :inline="true" :model="filters">
        <ElFormItem label="状态">
          <ElSelect v-model="filters.status" clearable placeholder="全部" style="width: 150px">
            <ElOption v-for="s in statusOptions" :key="s" :label="statusTag(s).label" :value="s" />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="SKU">
          <ElInput v-model="filters.sku" clearable placeholder="SKU" style="width: 160px" />
        </ElFormItem>
        <ElFormItem label="起始日期">
          <ElInput v-model="filters.from" clearable placeholder="2026-11-01" style="width: 140px" />
        </ElFormItem>
        <ElFormItem label="结束日期">
          <ElInput v-model="filters.to" clearable placeholder="2026-11-07" style="width: 140px" />
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" @click="onSearch">查询</ElButton>
          <ElButton @click="onReset">重置</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <ElCard class="table-card">
      <template #header>
        <div class="card-header">
          <span>共 {{ total }} 单 · 已选 {{ selected.length }} 单</span>
          <ElButton type="primary" :disabled="selected.length === 0" :loading="shipping" @click="batchShip">
            批量发货
          </ElButton>
        </div>
      </template>
      <ElTable :data="orders" border @selection-change="onSelectionChange">
        <ElTableColumn type="selection" width="50" />
        <ElTableColumn prop="orderNo" label="订单号" width="180" />
        <ElTableColumn prop="sku" label="SKU" width="130" />
        <ElTableColumn label="状态" width="120">
          <template #default="{ row }">
            <ElTag :type="statusTag(row.status).type">{{ statusTag(row.status).label }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="金额" width="120">
          <template #default="{ row }">¥ {{ Number(row.totalAmount).toFixed(2) }}</template>
        </ElTableColumn>
        <ElTableColumn prop="createdAtUtc" label="下单时间（UTC）" />
        <ElTableColumn prop="expiresAtUtc" label="过期时间（UTC）" />
        <ElTableColumn label="操作" width="120">
          <template #default="{ row }">
            <ElButton size="small" :disabled="row.status !== 'Created'" @click="cancelOne(row.id)">取消</ElButton>
          </template>
        </ElTableColumn>
        <template #empty>
          <ElEmpty description="没有符合条件的订单 · 可调整筛选条件或去商城下单" :image-size="70" />
        </template>
      </ElTable>
      <ElPagination
        v-model:current-page="filters.page"
        v-model:page-size="filters.pageSize"
        :total="total"
        :page-sizes="[10, 20, 50]"
        layout="total, sizes, prev, pager, next"
        @current-change="onPageChange"
        @size-change="onPageChange"
      />
    </ElCard>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import {
  ElCard,
  ElForm,
  ElFormItem,
  ElSelect,
  ElOption,
  ElInput,
  ElButton,
  ElTable,
  ElTableColumn,
  ElTag,
  ElEmpty,
  ElPagination,
  ElMessage,
} from 'element-plus'
import { api } from '../../api/client'
import type { OrderListItem, OrderStatus } from '../../api/types'
import { buildOrderQuery } from '../../utils/orderFilters'
import { statusTag } from '../../utils/statusTag'
import { usePolling } from '../../composables/usePolling'

const statusOptions: OrderStatus[] = [
  'Created', 'Paid', 'Shipped', 'Completed', 'Cancelled', 'Refunding', 'Refunded',
]

const filters = reactive({
  status: '',
  sku: '',
  from: '',
  to: '',
  page: 1,
  pageSize: 20,
})

const orders = ref<OrderListItem[]>([])
const total = ref(0)
const selected = ref<OrderListItem[]>([])
const shipping = ref(false)

async function load(): Promise<void> {
  try {
    const data = await api.listOrders(buildOrderQuery(filters))
    orders.value = data.items
    total.value = data.total
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '订单加载失败')
  }
}

function onSearch(): void {
  filters.page = 1
  void load()
}

function onPageChange(): void {
  void load()
}

function onReset(): void {
  filters.status = ''
  filters.sku = ''
  filters.from = ''
  filters.to = ''
  filters.page = 1
  filters.pageSize = 20
  void load()
}

function onSelectionChange(rows: unknown): void {
  selected.value = rows as OrderListItem[]
}

// 3 秒轮询；任何写操作（发货 / 取消）后立即 refresh() 重新拉取。
const polling = usePolling(load)

async function batchShip(): Promise<void> {
  shipping.value = true
  try {
    for (const row of selected.value) {
      await api.shipOrder(row.id)
    }
    ElMessage.success(`已发货 ${selected.value.length} 单`)
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '批量发货失败')
  } finally {
    shipping.value = false
    polling.refresh()
  }
}

async function cancelOne(id: string): Promise<void> {
  try {
    await api.cancelOrder(id)
    ElMessage.success('订单已取消')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '取消失败')
  } finally {
    polling.refresh()
  }
}
</script>

<style scoped>
.table-card {
  margin-top: 16px;
}
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
</style>
