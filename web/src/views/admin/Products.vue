<template>
  <div>
    <ElCard>
      <template #header>商品与库存（三态：可售 available / 预扣 reserved / 实扣 deducted）</template>
      <div class="toolbar">
        <ElButton type="primary" :disabled="selected.length === 0" @click="openPriceDialog">批量改价</ElButton>
        <span class="hint">已选 {{ selected.length }} 个 SKU</span>
      </div>
      <ElTable :data="products" border @selection-change="onSelectionChange">
        <ElTableColumn type="selection" width="50" />
        <ElTableColumn prop="sku" label="SKU" width="130" />
        <ElTableColumn prop="name" label="商品名称" />
        <ElTableColumn label="单价" width="120">
          <template #default="{ row }">¥ {{ Number(row.price).toFixed(2) }}</template>
        </ElTableColumn>
        <ElTableColumn prop="available" label="可售 available" width="140" />
        <ElTableColumn prop="reserved" label="预扣 reserved" width="140" />
        <ElTableColumn prop="deducted" label="实扣 deducted" width="140" />
        <ElTableColumn label="库存状态" width="130">
          <template #default="{ row }">
            <ElTag :type="stockView(row.available, row.reserved, row.deducted).type">
              {{ stockView(row.available, row.reserved, row.deducted).label }}
            </ElTag>
          </template>
        </ElTableColumn>
        <template #empty>
          <ElEmpty description="暂无商品 · 可在实验台一键重置重新播种 2 个 SKU" :image-size="70" />
        </template>
      </ElTable>
      <p class="hint">批量改价提交至 POST /api/products/prices；后端未实现该端点时会给出错误提示（演示环境容错）。</p>
    </ElCard>

    <ElDialog v-model="dialogVisible" title="批量改价" width="460px">
      <ElForm :model="priceForm">
        <ElFormItem label="新单价（¥）">
          <ElInputNumber v-model="priceForm.price" :min="0" :precision="2" :step="1" />
        </ElFormItem>
        <ElFormItem label="将修改">
          <span>{{ selectedSkuText }}</span>
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="dialogVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="saving" @click="applyPrice">确认改价</ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import {
  ElCard,
  ElButton,
  ElTable,
  ElTableColumn,
  ElTag,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInputNumber,
  ElEmpty,
  ElMessage,
} from 'element-plus'
import { api } from '../../api/client'
import type { Product } from '../../api/types'
import { stockView } from '../../utils/inventory'
import { usePolling } from '../../composables/usePolling'

const products = ref<Product[]>([])
const selected = ref<Product[]>([])
const dialogVisible = ref(false)
const saving = ref(false)
const priceForm = reactive({ price: 0 })
const selectedSkuText = computed(() => selected.value.map((p) => p.sku).join('、') || '（未选择）')

async function load(): Promise<void> {
  try {
    products.value = await api.listProducts()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '商品加载失败')
  }
}

// 3 秒轮询：库存由 Worker 异步实扣；改价写操作后立即 refresh()。
const polling = usePolling(load)

function onSelectionChange(rows: unknown): void {
  selected.value = rows as Product[]
}

function openPriceDialog(): void {
  dialogVisible.value = true
}

async function applyPrice(): Promise<void> {
  saving.value = true
  try {
    await api.updatePrices(selected.value.map((p) => ({ sku: p.sku, price: priceForm.price })))
    ElMessage.success('改价请求已提交')
    dialogVisible.value = false
    polling.refresh()
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '改价失败')
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 12px;
}
.hint {
  color: #909399;
  font-size: 13px;
}
</style>
