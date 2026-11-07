<template>
  <div class="shop-page">
    <!-- G1'：首屏不滚动就能看到定位、三个中间件、真实数字、三个入口。 -->
    <section class="shop-hero">
      <div>
        <span class="shop-hero__eyebrow">作品集演示 · 订单履约中台</span>
        <h1>一笔订单，跑通三个中间件</h1>
        <p class="shop-hero__desc">
          这里不是真实商城，而是一套可追问、可复现的订单履约中台。下单写进 MySQL 事务，
          消息经 outbox 补投到 RabbitMQ，Worker 消费时用 Redis 判重，最后落到库存流水与四方对账日报。
          下面每个数字都来自本系统真实跑出来的实验，不是示例值。
        </p>
        <div class="shop-hero__actions">
          <ElButton type="warning" @click="scrollToProducts">去下单</ElButton>
          <RouterLink to="/admin/dashboard"><ElButton plain>进运维后台</ElButton></RouterLink>
          <RouterLink to="/admin/lab"><ElButton plain>看实测数据</ElButton></RouterLink>
        </div>
      </div>
      <div class="shop-hero__diagram">
        <div class="shop-hero__diagram-title">一笔订单的真实链路</div>
        <div class="shop-flow">
          <span class="shop-flow__node">MySQL 事务</span><span class="shop-flow__arrow">→</span>
          <span class="shop-flow__node">outbox</span><span class="shop-flow__arrow">→</span>
          <span class="shop-flow__node">ex.orders</span><span class="shop-flow__arrow">→</span>
          <span class="shop-flow__node">q.fulfillment</span><span class="shop-flow__arrow">→</span>
          <span class="shop-flow__node">Worker</span><span class="shop-flow__arrow">→</span>
          <span class="shop-flow__node">库存实扣</span><span class="shop-flow__arrow">→</span>
          <span class="shop-flow__node">日报对账</span>
        </div>
        <div class="shop-note" style="margin-top: 14px; color: rgba(255,255,255,0.78)">
          浏览器不直连消息队列：页面状态靠 3 秒轮询，服务端内部才走 MQ。
        </div>
      </div>
    </section>

    <section class="shop-section">
      <div class="shop-section__head">
        <h2 class="shop-section__title">三个中间件，各解决一个真实问题</h2>
        <span class="shop-section__hint">点「看实测数据」可以自己复现下面每一个结论</span>
      </div>
      <div class="shop-mw__grid">
        <div class="shop-mw__card">
          <span class="shop-mw__tag">MySQL</span>
          <h3>下单不丢、账能对上</h3>
          <p>订单、明细、库存预扣、流水与 outbox 在同一事务里写入；乐观锁 version 挡住并发超卖，日报做四方对账。</p>
          <div class="shop-mw__keys">orders / order_items / inventory_ledgers / outbox</div>
        </div>
        <div class="shop-mw__card">
          <span class="shop-mw__tag">Redis</span>
          <h3>重复请求只生效一次</h3>
          <p>下单幂等短钥匙 idem:{token} 与消费端 dedup:{event_id} 用 SETNX 挡住重复点击和重复投递，配合数据库唯一索引双保险。</p>
          <div class="shop-mw__keys">idem:* / dedup:* / alert:seen:* / processed_events</div>
        </div>
        <div class="shop-mw__card">
          <span class="shop-mw__tag">RabbitMQ</span>
          <h3>异步履约与失败补偿</h3>
          <p>TTL + DLX 做超时关单，三次重试后进死信队列，人工补偿台可重投；publisher confirms 与手动 ack 保证不静默丢消息。</p>
          <div class="shop-mw__keys">q.fulfillment / q.fulfillment.retry / q.dlq.fulfillment</div>
        </div>
      </div>
    </section>

    <section class="shop-evidence">
      <div class="shop-evidence__head">
        <span>实测结论 · 数据来自 GET /api/experiments 的 experiment_results</span>
        <RouterLink to="/admin/lab" class="shop-chip">去实验台复现</RouterLink>
      </div>
      <div v-if="highlights && highlights.length > 0" class="shop-evidence__grid">
        <div v-for="item in highlights" :key="item.name" class="shop-evidence__item">
          <div class="shop-evidence__label">{{ item.name }} · {{ item.label }}</div>
          <div class="shop-evidence__value">{{ item.value }}</div>
          <div class="shop-evidence__caption">{{ item.caption }}</div>
          <div class="shop-evidence__caption">跑于 {{ item.ranAtUtc }}</div>
        </div>
      </div>
      <div v-else class="shop-empty" style="margin-top: 14px">
        暂无实测数据 · 请先在实验台运行 E1 / E2 / E3，本页只展示真实结果，不预填任何数字
      </div>
    </section>

    <section id="products" class="shop-section">
      <div class="shop-section__head">
        <div>
          <h2 class="shop-section__title">演示商品</h2>
          <div class="shop-section__hint">全部为模拟商品与模拟库存，加入购物车后可一次结算多明细订单</div>
        </div>
        <div class="shop-toolbar">
          <ElInput v-model="keyword" clearable placeholder="搜索名称 / SKU" style="width: 200px" />
          <ElSelect v-model="category" style="width: 130px">
            <ElOption label="全部状态" value="all" />
            <ElOption label="在售" value="active" />
            <ElOption label="停售" value="inactive" />
          </ElSelect>
          <ElSelect v-model="sortBy" style="width: 150px">
            <ElOption label="默认排序" value="default" />
            <ElOption label="价格从低到高" value="priceAsc" />
            <ElOption label="价格从高到低" value="priceDesc" />
            <ElOption label="可售库存最多" value="stockDesc" />
          </ElSelect>
        </div>
      </div>

      <ElAlert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <div v-else-if="loading" class="shop-empty">正在读取 /api/products …</div>
      <ElEmpty v-else-if="visibleProducts.length === 0" description="没有匹配的商品" />
      <div v-else class="product-grid">
        <article v-for="product in visibleProducts" :key="product.sku" class="product-card">
          <ProductThumb :sku="product.sku" :name="product.name" />
          <div class="product-card__body">
            <h3 class="product-card__name">{{ product.name }}</h3>
            <div class="product-card__sku">{{ product.sku }}</div>
            <div class="product-card__meta">
              <span class="shop-price"><small>¥</small> {{ Number(product.price).toFixed(2) }}</span>
              <span class="shop-stock">可售 {{ product.available }} · 预扣 {{ product.reserved }}</span>
            </div>
            <div class="product-card__actions">
              <ElButton size="small" @click="openDetail(product.sku)">查看详情</ElButton>
              <ElButton
                size="small"
                type="primary"
                :disabled="product.status !== 'active' || product.available <= 0"
                @click="addToCart(product)"
              >
                {{ product.available > 0 ? '加入购物车' : '已售罄' }}
              </ElButton>
            </div>
          </div>
        </article>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import {
  ElButton, ElInput, ElSelect, ElOption, ElAlert, ElEmpty, ElMessage,
} from 'element-plus'
import { api } from '../api/client'
import type { Product } from '../api/types'
import type { EvidenceHighlight } from '../utils/evidence'
import { parseLatestEvidence } from '../utils/evidence'
import { useCartStore } from '../stores/cart'
import ProductThumb from '../components/ProductThumb.vue'

const router = useRouter()
const cart = useCartStore()
const products = ref<Product[]>([])
const highlights = ref<EvidenceHighlight[] | null>(null)
const loading = ref(true)
const error = ref('')
const keyword = ref('')
const category = ref('all')
const sortBy = ref('default')

onMounted(async () => {
  try {
    products.value = await api.listProducts()
  } catch (e) {
    error.value = e instanceof Error ? e.message : '商品加载失败（请先启动后端 API）'
  } finally {
    loading.value = false
  }

  // 证据数字是「可选装饰」而非首屏依赖：接口失败 / 无实验记录一律显示「暂无实测数据」。
  try {
    highlights.value = parseLatestEvidence(await api.listExperiments())
  } catch {
    highlights.value = null
  }
})

const visibleProducts = computed(() => {
  const q = keyword.value.trim().toLowerCase()
  let list = products.value.filter((p) => {
    const matchKeyword = !q || p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q)
    const matchCategory = category.value === 'all' || p.status === category.value
    return matchKeyword && matchCategory
  })

  if (sortBy.value === 'priceAsc') list = [...list].sort((a, b) => Number(a.price) - Number(b.price))
  if (sortBy.value === 'priceDesc') list = [...list].sort((a, b) => Number(b.price) - Number(a.price))
  if (sortBy.value === 'stockDesc') list = [...list].sort((a, b) => b.available - a.available)
  return list
})

function addToCart(product: Product): void {
  cart.add(product)
  ElMessage.success(`${product.name} 已加入购物车（共 ${cart.totalCount} 件）`)
}

function openDetail(sku: string): void {
  router.push(`/product/${encodeURIComponent(sku)}`)
}

function scrollToProducts(): void {
  document.getElementById('products')?.scrollIntoView({ behavior: 'smooth' })
}
</script>
