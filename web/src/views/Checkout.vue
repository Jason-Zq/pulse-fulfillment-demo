<template>
  <div class="shop-page">
    <div class="shop-section__head">
      <h1 class="shop-section__title">确认订单</h1>
      <ElButton text @click="router.push('/cart')">← 返回购物车</ElButton>
    </div>

    <ElEmpty v-if="cart.isEmpty" description="购物车为空，无法结算">
      <ElButton type="primary" @click="router.push('/')">去商城</ElButton>
    </ElEmpty>

    <template v-else>
      <div class="shop-panel">
        <h2 class="shop-panel__title">全部商品明细（{{ cart.lines.length }} 种 / {{ cart.totalCount }} 件）</h2>
        <ElTable :data="cart.lines" border>
          <ElTableColumn prop="sku" label="SKU" width="150" />
          <ElTableColumn prop="name" label="商品" />
          <ElTableColumn label="单价" width="120">
            <template #default="{ row }">¥ {{ Number(row.price).toFixed(2) }}</template>
          </ElTableColumn>
          <ElTableColumn prop="quantity" label="数量" width="100" />
          <ElTableColumn label="小计" width="130">
            <template #default="{ row }">¥ {{ (Number(row.price) * Number(row.quantity)).toFixed(2) }}</template>
          </ElTableColumn>
        </ElTable>
        <div class="shop-summary">
          <span class="shop-total">应付 <strong>¥ {{ cart.totalAmount.toFixed(2) }}</strong></span>
        </div>
      </div>

      <div class="shop-panel">
        <h2 class="shop-panel__title">这次提交会真实发生什么</h2>
        <div class="shop-kv">
          <div class="shop-kv__item"><span class="shop-kv__k">请求体：</span><span class="shop-kv__v">{{ requestShape }}</span></div>
          <div class="shop-kv__item"><span class="shop-kv__k">幂等键：</span><span class="shop-kv__v shop-mono">{{ idempotencyKey }}</span></div>
          <div class="shop-kv__item"><span class="shop-kv__k">事务：</span><span class="shop-kv__v">orders + order_items + 库存预扣 + Reserve 流水 + outbox</span></div>
        </div>
        <div class="shop-note" style="margin-top: 12px">
          一次结算会话只生成一次幂等键；重复点击或网络重试会命中 orders.idempotency_key 唯一索引与 Redis idem:{token}，返回同一笔订单。
          提交后订单处于 Created，15 分钟内未支付会由 TTL + DLX 自动关单。
        </div>
        <div class="shop-row" style="margin-top: 18px">
          <ElButton type="primary" size="large" :loading="submitting" @click="submit">提交订单（模拟下单 · 演示）</ElButton>
          <span class="shop-note">不涉及真实支付、真实收款或真实发货</span>
        </div>
      </div>

      <TechEvidenceCard
        :middlewares="['MySQL', 'Redis']"
        :keys="['orders', 'order_items', 'inventory_ledgers', 'outbox', 'idem:{token}']"
        :reproduce="{ label: '去后台看这笔订单', path: '/admin/orders' }"
      />
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElButton, ElEmpty, ElTable, ElTableColumn, ElMessage } from 'element-plus'
import { api } from '../api/client'
import { useCartStore } from '../stores/cart'
import { buildPlaceOrder } from '../utils/checkout'
import TechEvidenceCard from '../components/TechEvidenceCard.vue'

const router = useRouter()
const cart = useCartStore()
const submitting = ref(false)

// 一次结算会话一个幂等键，重复点击 / 网络重试都复用它；多行购物车走 items[] 路径。
const prepared = buildPlaceOrder(cart.toOrderItems())
const idempotencyKey = prepared.idempotencyKey
const requestShape = computed(() => {
  // 用 sku 是否为键来判别：单 SKU 路径必带 sku，items[] 路径必不带。
  if ('sku' in prepared) {
    return `{ sku: ${prepared.sku}, quantity: ${prepared.quantity}, idempotencyKey }`
  }
  return `{ items: ${prepared.items.length} 行, idempotencyKey }`
})

async function submit(): Promise<void> {
  submitting.value = true
  try {
    const res = await api.placeOrder(prepared)
    if (!res.id) throw new Error('下单响应缺少订单 id')
    cart.clear()
    ElMessage.success(`订单 ${res.orderNo} 已创建`)
    router.push(`/order/${encodeURIComponent(res.id)}`)
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '下单失败')
  } finally {
    submitting.value = false
  }
}
</script>
