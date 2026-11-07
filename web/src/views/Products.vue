<template>
  <div class="shop-page">
    <!-- 消费优先首屏：轮播 banner + 分类导航，全部为模拟数据。 -->
    <section class="shop-carousel">
      <ElCarousel height="288px" :interval="6000" arrow="hover">
        <ElCarouselItem v-for="slide in slides" :key="slide.title">
          <div class="slide" :style="{ background: slide.bg }">
            <div class="slide__inner">
              <span class="slide__eyebrow">{{ slide.eyebrow }}</span>
              <h1 class="slide__title">{{ slide.title }}</h1>
              <p class="slide__desc">{{ slide.desc }}</p>
              <div class="slide__actions">
                <ElButton type="warning" @click="scrollToProducts">立即选购</ElButton>
                <RouterLink :to="slide.to"><ElButton plain>逛该分类</ElButton></RouterLink>
              </div>
            </div>
            <div class="slide__deco" aria-hidden="true">
              <div class="slide__deco-ring"></div>
              <div class="slide__deco-ring slide__deco-ring--sm"></div>
              <span class="slide__deco-text">PULSE · DEMO</span>
            </div>
          </div>
        </ElCarouselItem>
      </ElCarousel>
    </section>

    <section class="shop-section">
      <div class="shop-section__head">
        <h2 class="shop-section__title">按分类逛</h2>
        <span class="shop-section__hint">全部为模拟商品与模拟库存 · 不涉及真实交易</span>
      </div>
      <div class="category-grid">
        <RouterLink
          v-for="c in PRODUCT_CATEGORIES"
          :key="c"
          class="category-card"
          :to="{ path: '/', query: { category: c } }"
        >
          <span class="category-card__icon"><CategoryIcon :category="c" /></span>
          <span class="category-card__name">{{ c }}</span>
          <span class="category-card__count">{{ categoryCount(c) }} 件在售</span>
        </RouterLink>
      </div>
    </section>

    <section class="shop-section">
      <div class="shop-section__head">
        <h2 class="shop-section__title">猜你喜欢</h2>
        <span class="shop-section__hint">演示推荐：每个分类各挑一件，不基于真实用户行为</span>
      </div>
      <div class="product-grid">
        <article v-for="product in recommended" :key="product.sku" class="product-card">
          <RouterLink :to="`/product/${encodeURIComponent(product.sku)}`" class="home-card__link">
            <ProductThumb :sku="product.sku" :name="product.name" />
            <div class="product-card__body">
              <h3 class="product-card__name">{{ product.name }}</h3>
              <div class="product-card__sku">{{ product.category }} · {{ product.sku }}</div>
              <div class="product-card__meta">
                <span class="shop-price"><small>¥</small> {{ Number(product.price).toFixed(2) }}</span>
                <span class="shop-stock">可售 {{ product.available }}</span>
              </div>
            </div>
          </RouterLink>
        </article>
      </div>
    </section>

    <section id="products" class="shop-section">
      <div class="shop-section__head">
        <div>
          <h2 class="shop-section__title">全部商品</h2>
          <div class="shop-section__hint">加入购物车后可一次结算多明细订单</div>
        </div>
        <div class="shop-toolbar">
          <span class="shop-result-count">共 {{ visibleProducts.length }} 件商品</span>
          <div class="shop-spacer"></div>
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
          <RouterLink :to="`/product/${encodeURIComponent(product.sku)}`" class="home-card__link">
            <ProductThumb :sku="product.sku" :name="product.name" />
          </RouterLink>
          <div class="product-card__body">
            <h3 class="product-card__name">{{ product.name }}</h3>
            <div class="product-card__sku">{{ product.category }} · {{ product.sku }}</div>
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

    <!-- 技术叙事降级为暗线：默认折叠，作品集追问时再展开。 -->
    <section class="shop-section">
      <ElCollapse v-model="techOpen" class="home-tech">
        <ElCollapseItem title="技术溯源 · 关于这个演示站（点击展开）" name="tech">
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
        </ElCollapseItem>
      </ElCollapse>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter, RouterLink } from 'vue-router'
import {
  ElButton, ElSelect, ElOption, ElAlert, ElEmpty, ElMessage,
  ElCarousel, ElCarouselItem, ElCollapse, ElCollapseItem,
} from 'element-plus'
import { api } from '../api/client'
import type { Product } from '../api/types'
import type { EvidenceHighlight } from '../utils/evidence'
import { parseLatestEvidence } from '../utils/evidence'
import { filterProducts } from '../utils/productFilter'
import { PRODUCT_CATEGORIES } from '../utils/categories'
import { useCartStore } from '../stores/cart'
import ProductThumb from '../components/ProductThumb.vue'
import CategoryIcon from '../components/CategoryIcon.vue'

interface Slide {
  eyebrow: string
  title: string
  desc: string
  bg: string
  to: { path: string; query: { category: string } }
}

const slides: Slide[] = [
  {
    eyebrow: '演示商城 · 全部数据为模拟',
    title: '数码好物，随手加购',
    desc: '耳机、键盘与更多模拟商品，加入购物车即可一次结算多明细订单，全程不涉及真实交易。',
    bg: 'linear-gradient(120deg, #1d2b64 0%, #3b5bdb 58%, #5572ec 100%)',
    to: { path: '/', query: { category: '数码电器' } },
  },
  {
    eyebrow: '分类逛 · 5 个品类',
    title: '把家布置成喜欢的样子',
    desc: '落地灯、家居小物等演示商品，价格与库存均为模拟值，来自 GET /api/products 的真实响应。',
    bg: 'linear-gradient(120deg, #0f766e 0%, #14b8a6 55%, #5eead4 100%)',
    to: { path: '/', query: { category: '家居生活' } },
  },
  {
    eyebrow: '可追问 · 可复现',
    title: '每一笔订单都看得见链路',
    desc: '下单、模拟支付、发货与超时关单都能在后台追到 MySQL / Redis / RabbitMQ 的真实证据。',
    bg: 'linear-gradient(120deg, #7c2d12 0%, #ea580c 55%, #fb923c 100%)',
    to: { path: '/', query: { category: '食品生鲜' } },
  },
]

const route = useRoute()
const router = useRouter()
const cart = useCartStore()
const products = ref<Product[]>([])
const highlights = ref<EvidenceHighlight[] | null>(null)
const loading = ref(true)
const error = ref('')
const sortBy = ref<'default' | 'priceAsc' | 'priceDesc' | 'stockDesc'>('default')
const techOpen = ref<string[]>([])

// 关键字与分类由顶栏全局搜索 / 分类导航通过路由 query 驱动，首页只负责消费与排序。
const keyword = computed(() => (typeof route.query.q === 'string' ? route.query.q : ''))
const category = computed(() =>
  typeof route.query.category === 'string' && route.query.category !== 'all'
    ? route.query.category
    : 'all',
)

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

const visibleProducts = computed(() =>
  filterProducts(products.value, {
    keyword: keyword.value,
    category: category.value,
    sortBy: sortBy.value,
  }),
)

function categoryCount(name: string): number {
  return products.value.filter((p) => p.category === name).length
}

// 演示推荐：每个分类各挑一件，纯前端挑选，不伪造「销量」或用户行为数据。
const recommended = computed(() => {
  const picked: Product[] = []
  for (const name of PRODUCT_CATEGORIES) {
    const first = products.value.find((p) => p.category === name && p.status === 'active')
    if (first) picked.push(first)
  }
  return picked
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

<style scoped>
.shop-carousel { border-radius: 20px; overflow: hidden; box-shadow: var(--shop-shadow); }
.slide {
  height: 100%; display: flex; align-items: center; justify-content: space-between;
  gap: 20px; padding: 34px 44px; color: #fff;
}
.slide__inner { max-width: 560px; }
.slide__eyebrow {
  display: inline-block; padding: 4px 10px; border: 1px solid rgba(255,255,255,0.4);
  border-radius: 999px; font-size: 12px; letter-spacing: 0.06em;
}
.slide__title { margin: 16px 0 10px; font-size: 32px; line-height: 1.25; font-weight: 650; }
.slide__desc { margin: 0; font-size: 14px; line-height: 1.8; color: rgba(255,255,255,0.88); }
.slide__actions { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 20px; }
.slide__actions .el-button { border-radius: 9px; font-weight: 500; }
.slide__deco { position: relative; width: 220px; height: 200px; flex: none; }
.slide__deco-ring {
  position: absolute; inset: 0; margin: auto; width: 180px; height: 180px;
  border: 18px solid rgba(255,255,255,0.16); border-radius: 50%;
}
.slide__deco-ring--sm { width: 96px; height: 96px; border-width: 12px; border-color: rgba(255,255,255,0.28); }
.slide__deco-text {
  position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%);
  font-size: 12px; letter-spacing: 0.2em; color: rgba(255,255,255,0.85);
}
.category-grid { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 12px; }
.category-card {
  display: flex; flex-direction: column; align-items: center; gap: 8px;
  padding: 18px 10px; border-radius: var(--shop-radius); text-decoration: none;
  background: var(--shop-surface); border: 1px solid var(--shop-line);
  box-shadow: var(--shop-shadow); transition: transform 0.18s ease, border-color 0.18s ease;
}
.category-card:hover { transform: translateY(-3px); border-color: var(--shop-brand); }
.category-card__icon {
  display: flex; align-items: center; justify-content: center;
  width: 46px; height: 46px; padding: 8px; border-radius: 14px;
  background: var(--shop-brand-soft); color: var(--shop-brand-deep);
}
.category-card__name { font-size: 14px; font-weight: 600; color: var(--shop-ink); }
.category-card__count { font-size: 12px; color: var(--shop-muted); }
.home-card__link { display: block; text-decoration: none; color: inherit; }
.home-tech :deep(.el-collapse-item__content) { padding: 0 4px 8px; }
@media (max-width: 860px) {
  .slide { padding: 24px; }
  .slide__deco { display: none; }
  .slide__title { font-size: 24px; }
  .category-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
@media (max-width: 560px) {
  .category-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
