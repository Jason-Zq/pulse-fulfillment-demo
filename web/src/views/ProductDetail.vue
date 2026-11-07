<template>
  <div class="shop-page">
    <div class="shop-row" style="margin-bottom: 16px">
      <ElButton text @click="router.push('/')">← 返回商城</ElButton>
    </div>

    <ElAlert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div v-else-if="!product" class="shop-empty">正在读取商品与库存 …</div>

    <div v-else class="shop-detail">
      <div class="shop-panel shop-detail__media">
        <ProductThumb :sku="product.sku" :name="product.name" />
        <div class="shop-note" style="margin-top: 12px">
          图为内联 SVG 占位图，零外部资源；价格与库存均为模拟数据。
        </div>
      </div>

      <div class="shop-detail__info">
        <div class="shop-panel">
          <h1 class="shop-section__title">{{ product.name }}</h1>
          <div class="product-card__sku" style="margin: 6px 0 18px">{{ product.sku }}</div>
          <div class="shop-price" style="margin-bottom: 16px">
            <small>¥</small> {{ Number(product.price).toFixed(2) }}
          </div>

          <div class="shop-stock-grid">
            <div class="shop-stock-cell">
              <div class="shop-kv__k">available · 可售</div>
              <div class="shop-stock-value">{{ product.available }}</div>
            </div>
            <div class="shop-stock-cell">
              <div class="shop-kv__k">reserved · 预扣</div>
              <div class="shop-stock-value">{{ product.reserved }}</div>
            </div>
            <div class="shop-stock-cell">
              <div class="shop-kv__k">deducted · 已实扣</div>
              <div class="shop-stock-value">{{ product.deducted }}</div>
            </div>
            <div class="shop-stock-cell">
              <div class="shop-kv__k">version · 乐观锁</div>
              <div class="shop-stock-value">{{ product.version ?? '—' }}</div>
            </div>
          </div>
          <div class="shop-note" style="margin-top: 12px">
            每次预扣 / 实扣都会让 Inventory.Version 自增；并发拿到旧版本会被乐观锁拒绝，因此不会超卖。
          </div>

          <div class="shop-row" style="margin-top: 20px">
            <ElButton size="small" :disabled="quantity <= 1" @click="quantity -= 1">−</ElButton>
            <span class="shop-mono" style="min-width: 34px; text-align: center">{{ quantity }}</span>
            <ElButton size="small" :disabled="quantity >= product.available" @click="quantity += 1">＋</ElButton>
            <span class="shop-note">最多 {{ product.available }} 件</span>
            <div class="shop-spacer"></div>
            <ElButton
              type="primary"
              :disabled="product.status !== 'active' || product.available <= 0"
              @click="addToCart"
            >
              加入购物车
            </ElButton>
          </div>
        </div>

        <TechEvidenceCard
          :middlewares="['MySQL', 'Redis']"
          :keys="['products', 'inventories', 'inventory_ledgers', 'Inventory.Version', 'idem:{token}']"
          :reproduce="{ label: '去实验台跑 E3 并发不超卖', path: '/admin/lab' }"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElButton, ElAlert, ElMessage } from 'element-plus'
import { api } from '../api/client'
import type { Product } from '../api/types'
import { useCartStore } from '../stores/cart'
import ProductThumb from '../components/ProductThumb.vue'
import TechEvidenceCard from '../components/TechEvidenceCard.vue'

const route = useRoute()
const router = useRouter()
const cart = useCartStore()
const sku = String(route.params.sku ?? '')
const product = ref<Product | null>(null)
const error = ref('')
const quantity = ref(1)

onMounted(async () => {
  try {
    const list = await api.listProducts()
    product.value = list.find((p) => p.sku === sku) ?? null
    if (!product.value) error.value = `找不到 SKU：${sku}`
  } catch (e) {
    error.value = e instanceof Error ? e.message : '商品加载失败'
  }
})

function addToCart(): void {
  if (!product.value) return
  cart.add(product.value, quantity.value)
  ElMessage.success(`${product.value.name} × ${quantity.value} 已加入购物车`)
}
</script>

<style scoped>
.shop-detail {
  display: grid;
  grid-template-columns: minmax(0, 0.9fr) minmax(0, 1.1fr);
  gap: 18px;
  align-items: start;
}
.shop-detail__media { padding: 16px; }
.shop-detail__info { display: flex; flex-direction: column; gap: 16px; }
.shop-stock-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; }
.shop-stock-cell {
  border: 1px solid var(--shop-line); border-radius: 10px; padding: 12px; background: #fbfcff;
}
.shop-kv__k { font-size: 12px; color: var(--shop-muted); }
.shop-stock-value { margin-top: 6px; font-size: 22px; font-weight: 650; color: var(--shop-brand-deep); }
@media (max-width: 860px) {
  .shop-detail { grid-template-columns: 1fr; }
  .shop-stock-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
