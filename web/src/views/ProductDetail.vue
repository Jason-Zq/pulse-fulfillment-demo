<template>
  <div class="shop-page">
    <ElBreadcrumb separator="/" class="detail-crumb">
      <ElBreadcrumbItem :to="{ path: '/' }">商城首页</ElBreadcrumbItem>
      <ElBreadcrumbItem v-if="product" :to="{ path: '/', query: { category: product.category } }">
        {{ product.category }}
      </ElBreadcrumbItem>
      <ElBreadcrumbItem>{{ product?.name ?? '商品详情' }}</ElBreadcrumbItem>
    </ElBreadcrumb>

    <ElAlert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div v-else-if="!product" class="shop-empty">正在读取商品与库存 …</div>

    <div v-else class="shop-detail">
      <div class="shop-panel shop-detail__media">
        <ProductGallery :sku="product.sku" :name="product.name" />
      </div>

      <div class="shop-detail__info">
        <div class="shop-panel">
          <div class="detail-tags">
            <span class="shop-chip">{{ product.category }}</span>
            <span class="detail-tag">演示商品 · 非真实交易</span>
          </div>
          <h1 class="detail-name">{{ product.name }}</h1>
          <div class="product-card__sku">{{ product.sku }}</div>
          <div class="detail-price"><small>¥</small> {{ Number(product.price).toFixed(2) }}</div>

          <div class="detail-stats">
            <div class="detail-stat">
              <span class="detail-stat__k">已实扣</span>
              <strong class="detail-stat__v">{{ product.deducted }}</strong>
              <span class="detail-stat__note">真实流水累计</span>
            </div>
            <div class="detail-stat">
              <span class="detail-stat__k">可售库存</span>
              <strong class="detail-stat__v">{{ product.available }}</strong>
              <span class="detail-stat__note">实时库存</span>
            </div>
            <div class="detail-stat">
              <span class="detail-stat__k">状态</span>
              <strong class="detail-stat__v detail-stat__v--text">
                {{ product.status === 'active' && product.available > 0 ? '现货' : '暂不可售' }}
              </strong>
              <span class="detail-stat__note">模拟库存</span>
            </div>
          </div>

          <div class="detail-qty">
            <span class="detail-qty__label">数量</span>
            <ElButton size="small" :disabled="quantity <= 1" @click="quantity -= 1">−</ElButton>
            <span class="shop-mono" style="min-width: 34px; text-align: center">{{ quantity }}</span>
            <ElButton size="small" :disabled="quantity >= product.available" @click="quantity += 1">＋</ElButton>
            <span class="shop-note">最多 {{ product.available }} 件</span>
          </div>

          <div class="detail-actions">
            <ElButton
              type="primary"
              size="large"
              :disabled="product.status !== 'active' || product.available <= 0"
              @click="addToCart"
            >
              加入购物车
            </ElButton>
            <ElButton size="large" @click="router.push('/cart')">去购物车结算</ElButton>
          </div>

          <div class="detail-services">
            <span v-for="s in services" :key="s" class="detail-service">{{ s }}</span>
          </div>
          <div class="shop-note" style="margin-top: 10px">
            以上服务均为演示说明，本站不产生真实售后、物流或支付。
          </div>
        </div>

        <!-- 技术叙事降级为暗线：默认折叠，点开才看库存三态与中间件证据。 -->
        <ElCollapse v-model="techOpen" class="detail-tech">
          <ElCollapseItem title="技术溯源 · 库存三态 / 乐观锁 / 链路证据（点击展开）" name="tech">
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
            <TechEvidenceCard
              class="detail-tech__card"
              :middlewares="['MySQL', 'Redis']"
              :keys="['products', 'inventories', 'inventory_ledgers', 'Inventory.Version', 'idem:{token}']"
              :reproduce="{ label: '去实验台跑 E3 并发不超卖', path: '/admin/lab' }"
            />
          </ElCollapseItem>
        </ElCollapse>
      </div>
    </div>

    <section v-if="product && related.length > 0" class="shop-section">
      <div class="shop-section__head">
        <h2 class="shop-section__title">同类推荐 · {{ product.category }}</h2>
        <RouterLink class="shop-chip" :to="{ path: '/', query: { category: product.category } }">
          查看该分类全部商品 →
        </RouterLink>
      </div>
      <div class="product-grid">
        <article v-for="item in related" :key="item.sku" class="product-card">
          <RouterLink :to="`/product/${encodeURIComponent(item.sku)}`" class="detail-related__link">
            <ProductThumb :sku="item.sku" :name="item.name" />
            <div class="product-card__body">
              <h3 class="product-card__name">{{ item.name }}</h3>
              <div class="product-card__sku">{{ item.sku }}</div>
              <div class="product-card__meta">
                <span class="shop-price"><small>¥</small> {{ Number(item.price).toFixed(2) }}</span>
                <span class="shop-stock">可售 {{ item.available }}</span>
              </div>
            </div>
          </RouterLink>
        </article>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter, RouterLink } from 'vue-router'
import {
  ElButton, ElAlert, ElMessage, ElBreadcrumb, ElBreadcrumbItem,
  ElCollapse, ElCollapseItem,
} from 'element-plus'
import { api } from '../api/client'
import type { Product } from '../api/types'
import { useCartStore } from '../stores/cart'
import ProductThumb from '../components/ProductThumb.vue'
import ProductGallery from '../components/ProductGallery.vue'
import TechEvidenceCard from '../components/TechEvidenceCard.vue'

const route = useRoute()
const router = useRouter()
const cart = useCartStore()
const sku = computed(() => String(route.params.sku ?? ''))
const product = ref<Product | null>(null)
const allProducts = ref<Product[]>([])
const error = ref('')
const quantity = ref(1)
const techOpen = ref<string[]>([])
const services = ['模拟发货 · 演示', '15 分钟未支付自动关单', '全链路可复现', '零真实支付']

async function load(): Promise<void> {
  error.value = ''
  product.value = null
  quantity.value = 1
  try {
    const list = await api.listProducts()
    allProducts.value = list
    const found = list.find((p) => p.sku === sku.value) ?? null
    if (!found) error.value = `找不到 SKU：${sku.value}`
    else product.value = found
  } catch (e) {
    error.value = e instanceof Error ? e.message : '商品加载失败'
  }
}

onMounted(load)
// 从「同类推荐」跳到另一 SKU 时路由组件复用，必须重新加载。
watch(sku, load)

const related = computed(() => {
  const current = product.value
  if (!current) return []
  return allProducts.value
    .filter((p) => p.category === current.category && p.sku !== current.sku)
    .slice(0, 4)
})

function addToCart(): void {
  if (!product.value) return
  cart.add(product.value, quantity.value)
  ElMessage.success(`${product.value.name} × ${quantity.value} 已加入购物车`)
}
</script>

<style scoped>
.detail-crumb { margin-bottom: 18px; font-size: 13px; }
.shop-detail {
  display: grid;
  grid-template-columns: minmax(0, 0.9fr) minmax(0, 1.1fr);
  gap: 18px;
  align-items: start;
}
.shop-detail__media { padding: 16px; }
.shop-detail__info { display: flex; flex-direction: column; gap: 16px; }
.detail-tags { display: flex; flex-wrap: wrap; gap: 8px; }
.detail-tag {
  padding: 2px 8px; border-radius: 6px; font-size: 12px;
  background: #fff4e8; color: #b45309;
}
.detail-name { margin: 12px 0 6px; font-size: 26px; font-weight: 650; color: var(--shop-ink); }
.detail-price { margin: 16px 0 20px; color: var(--shop-accent); font-size: 32px; font-weight: 700; }
.detail-price small { font-size: 16px; font-weight: 500; }
.detail-stats {
  display: grid; grid-template-columns: repeat(3, minmax(0, 1fr));
  border-top: 1px solid var(--shop-line); border-bottom: 1px solid var(--shop-line);
  padding: 14px 0; gap: 10px;
}
.detail-stat { display: flex; flex-direction: column; align-items: center; border-right: 1px solid var(--shop-line); }
.detail-stat:last-child { border-right: none; }
.detail-stat__k { font-size: 12px; color: var(--shop-muted); }
.detail-stat__v { margin: 4px 0; font-size: 22px; font-weight: 680; color: var(--shop-brand-deep); }
.detail-stat__v--text { font-size: 16px; }
.detail-stat__note { font-size: 11px; color: #94a3b8; }
.detail-qty { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin: 20px 0; }
.detail-qty__label { min-width: 40px; font-size: 13px; color: var(--shop-muted); }
.detail-actions { display: flex; flex-wrap: wrap; gap: 10px; }
.detail-actions .el-button { border-radius: 9px; }
.detail-services { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 20px; }
.detail-service {
  padding: 4px 10px; border-radius: 6px; font-size: 12px;
  border: 1px solid var(--shop-line); color: var(--shop-muted);
}
.shop-stock-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; }
.shop-stock-cell {
  border: 1px solid var(--shop-line); border-radius: 10px; padding: 12px; background: #fbfcff;
}
.shop-kv__k { font-size: 12px; color: var(--shop-muted); }
.shop-stock-value { margin-top: 6px; font-size: 22px; font-weight: 650; color: var(--shop-brand-deep); }
.detail-tech__card { margin-top: 14px; }
.detail-related__link { text-decoration: none; color: inherit; display: block; }
@media (max-width: 860px) {
  .shop-detail { grid-template-columns: 1fr; }
  .shop-stock-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
