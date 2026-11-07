<template>
  <div class="shop-page">
    <div class="shop-section__head">
      <h1 class="shop-section__title">确认订单</h1>
      <ElButton text @click="router.push('/cart')">← 返回购物车</ElButton>
    </div>

    <ElEmpty v-if="checkoutLines.length === 0" description="没有已勾选的商品，无法结算">
      <ElButton type="primary" @click="router.push('/cart')">回购物车勾选</ElButton>
    </ElEmpty>

    <template v-else>
      <div class="shop-panel">
        <h2 class="shop-panel__title">收货信息 <span class="demo-tag">模拟 · 演示</span></h2>
        <div class="checkout-addresses">
          <label
            v-for="addr in addresses"
            :key="addr.id"
            class="checkout-address"
            :class="{ 'is-active': addressId === addr.id }"
          >
            <input v-model="addressId" type="radio" :value="addr.id" />
            <span class="checkout-address__name">{{ addr.name }} · {{ addr.phone }}</span>
            <span class="checkout-address__detail">{{ addr.detail }}</span>
            <span v-if="addr.default" class="shop-chip">默认</span>
          </label>
        </div>
        <div class="shop-note" style="margin-top: 10px">
          地址为演示占位数据，不会写入订单、不参与真实履约与发货；本站不收集真实个人信息。
        </div>
      </div>

      <div class="shop-panel">
        <h2 class="shop-panel__title">已选商品明细（{{ checkoutLines.length }} 种 / {{ selectedCount }} 件）</h2>
        <ElTable :data="checkoutLines" border>
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
      </div>

      <div class="shop-panel">
        <h2 class="shop-panel__title">配送与优惠 <span class="demo-tag">模拟 · 演示</span></h2>
        <div class="checkout-field">
          <span class="checkout-field__label">配送时间</span>
          <ElRadioGroup v-model="delivery">
            <ElRadio value="any">任意时间</ElRadio>
            <ElRadio value="workday">仅工作日</ElRadio>
            <ElRadio value="weekend">仅周末</ElRadio>
          </ElRadioGroup>
        </div>
        <div class="checkout-field">
          <span class="checkout-field__label">优惠券</span>
          <ElSelect v-model="coupon" style="width: 240px">
            <ElOption label="不使用优惠券" value="none" />
            <ElOption label="满 200 减 20（演示）" value="s20" />
            <ElOption label="满 500 减 50（演示）" value="s50" />
          </ElSelect>
          <span class="shop-note">仅作界面演示，不会进入订单金额</span>
        </div>
        <div class="checkout-field">
          <span class="checkout-field__label">支付方式</span>
          <ElRadioGroup v-model="payment">
            <ElRadio value="mock">模拟支付 · 演示</ElRadio>
          </ElRadioGroup>
        </div>
        <div class="checkout-amount">
          <div class="checkout-amount__row"><span>商品金额</span><span>¥ {{ cart.selectedTotalAmount.toFixed(2) }}</span></div>
          <div class="checkout-amount__row shop-note"><span>演示优惠（不参与下单）</span><span>− ¥ {{ demoDiscount.toFixed(2) }}</span></div>
          <div class="checkout-amount__row shop-note"><span>演示运费（不参与下单）</span><span>¥ 0.00</span></div>
          <div class="checkout-amount__row checkout-amount__total">
            <span>实际应付（后端按商品价格计算）</span>
            <strong>¥ {{ cart.selectedTotalAmount.toFixed(2) }}</strong>
          </div>
        </div>
        <div class="shop-note" style="margin-top: 10px">
          优惠券、运费、配送时间与支付方式均为前端演示项，不写入请求体；提交后订单金额以
          POST /api/orders 的真实计算为准，本站不涉及真实支付、收款或发货。
        </div>
      </div>

      <ElCollapse v-model="techOpen" class="checkout-tech">
        <ElCollapseItem title="技术溯源 · 这次提交会真实发生什么（点击展开）" name="tech">
          <div class="shop-kv">
            <div class="shop-kv__item"><span class="shop-kv__k">请求体：</span><span class="shop-kv__v">{{ requestShape }}</span></div>
            <div class="shop-kv__item"><span class="shop-kv__k">幂等键：</span><span class="shop-kv__v shop-mono">{{ idempotencyKey }}</span></div>
            <div class="shop-kv__item"><span class="shop-kv__k">事务：</span><span class="shop-kv__v">orders + order_items + 库存预扣 + Reserve 流水 + outbox</span></div>
          </div>
          <div class="shop-note" style="margin-top: 12px">
            一次结算会话只生成一次幂等键；重复点击或网络重试会命中 orders.idempotency_key 唯一索引与 Redis idem:{token}，返回同一笔订单。
            提交后订单处于 Created，15 分钟内未支付会由 TTL + DLX 自动关单。
          </div>
        </ElCollapseItem>
      </ElCollapse>

      <TechEvidenceCard
        :middlewares="['MySQL', 'Redis']"
        :keys="['orders', 'order_items', 'inventory_ledgers', 'outbox', 'idem:{token}']"
        :reproduce="{ label: '去后台看这笔订单', path: '/admin/orders' }"
      />

      <div class="checkout-bar">
        <span class="shop-total">应付 <strong>¥ {{ cart.selectedTotalAmount.toFixed(2) }}</strong></span>
        <span class="shop-note">共 {{ selectedCount }} 件 · 不涉及真实支付</span>
        <div class="shop-spacer"></div>
        <ElButton type="primary" size="large" :loading="submitting" @click="submit">
          提交订单（模拟下单 · 演示）
        </ElButton>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  ElButton, ElEmpty, ElTable, ElTableColumn, ElMessage,
  ElRadio, ElRadioGroup, ElSelect, ElOption, ElCollapse, ElCollapseItem,
} from 'element-plus'
import { api } from '../api/client'
import { useCartStore } from '../stores/cart'
import { buildPlaceOrder } from '../utils/checkout'
import TechEvidenceCard from '../components/TechEvidenceCard.vue'

const router = useRouter()
const cart = useCartStore()
const submitting = ref(false)

interface Address {
  id: string
  name: string
  phone: string
  detail: string
  default: boolean
}
const addresses: Address[] = [
  { id: 'a1', name: '演示收件人', phone: '138****0000', detail: '演示省 演示市 演示区 演示路 1 号', default: true },
  { id: 'a2', name: '备用收件人', phone: '139****0000', detail: '演示省 演示市 演示区 演示路 2 号', default: false },
]
const addressId = ref('a1')
const delivery = ref('any')
const coupon = ref('none')
const payment = ref('mock')
const techOpen = ref<string[]>([])

const checkoutLines = computed(() => cart.selectedLines)
const selectedCount = computed(() => cart.selectedTotalCount)

// 演示优惠只做视觉计算，绝不进入请求体或应付金额。
const demoDiscount = computed(() => {
  const total = cart.selectedTotalAmount
  if (coupon.value === 's20' && total >= 200) return 20
  if (coupon.value === 's50' && total >= 500) return 50
  return 0
})

// 一次结算会话一个幂等键，重复点击 / 网络重试都复用它；只提交已勾选的明细（items[] 路径）。
const prepared = buildPlaceOrder(cart.selectedItems())
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

<style scoped>
.demo-tag {
  margin-left: 8px; padding: 2px 8px; border-radius: 6px;
  background: #fff4e8; color: #b45309; font-size: 12px; font-weight: 500;
}
.checkout-addresses { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 12px; }
.checkout-address {
  display: flex; flex-wrap: wrap; align-items: center; gap: 8px;
  border: 1px solid var(--shop-line); border-radius: 10px; padding: 12px 14px;
  background: #fbfcff; cursor: pointer;
}
.checkout-address.is-active { border-color: var(--shop-brand); background: var(--shop-brand-soft); }
.checkout-address__name { font-size: 14px; font-weight: 600; color: var(--shop-ink); }
.checkout-address__detail { flex-basis: 100%; font-size: 12px; color: var(--shop-muted); }
.checkout-field { display: flex; flex-wrap: wrap; align-items: center; gap: 14px; margin-bottom: 14px; }
.checkout-field__label { min-width: 76px; font-size: 13px; color: var(--shop-muted); }
.checkout-amount {
  margin-top: 8px; padding: 14px 16px; border-radius: 10px; background: #fbfcff;
  border: 1px solid var(--shop-line); max-width: 420px; margin-left: auto;
}
.checkout-amount__row { display: flex; justify-content: space-between; gap: 20px; padding: 4px 0; font-size: 13px; }
.checkout-amount__total { border-top: 1px solid var(--shop-line); margin-top: 6px; padding-top: 10px; font-weight: 600; }
.checkout-amount__total strong { color: var(--shop-accent); font-size: 20px; }
.checkout-tech { margin-top: 16px; }
.checkout-bar {
  position: sticky; bottom: 0; z-index: 5;
  display: flex; flex-wrap: wrap; align-items: center; gap: 14px;
  margin-top: 16px; padding: 14px 18px; border-radius: var(--shop-radius);
  background: #fff; border: 1px solid var(--shop-line); box-shadow: var(--shop-shadow);
}
</style>
