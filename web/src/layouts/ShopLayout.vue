<template>
  <div class="shop-layout">
    <ElAlert :title="DEMO_BANNER" type="warning" :closable="false" show-icon />
    <div class="shop-header">
      <header class="shop-topbar">
        <RouterLink class="brand" to="/">
          <span class="brand-mark">P</span>
          <span>Pulse<span class="brand-sub"> · 演示商城</span></span>
        </RouterLink>
        <form class="shop-search" @submit.prevent="submitSearch">
          <ElInput
            v-model="search"
            class="shop-search__input"
            placeholder="搜索商品名称 / SKU"
            clearable
            @keyup.enter="submitSearch"
          >
            <template #append>
              <ElButton type="primary" @click="submitSearch">搜索</ElButton>
            </template>
          </ElInput>
        </form>
        <nav class="shop-topnav">
          <RouterLink to="/tour" class="shop-topnav__tech">技术演示</RouterLink>
          <RouterLink to="/orders">我的订单</RouterLink>
          <RouterLink to="/member">会员中心</RouterLink>
          <RouterLink to="/admin/dashboard" class="shop-topnav__optional">运营后台</RouterLink>
          <RouterLink to="/about" class="shop-topnav__optional">关于</RouterLink>
        </nav>
        <RouterLink class="cart-link" to="/cart">
          <span class="cart-label">购物车</span>
          <span class="cart-badge">{{ cart.totalCount }}</span>
        </RouterLink>
      </header>
      <nav class="shop-catbar">
        <RouterLink
          class="shop-catbar__item"
          :class="{ 'is-active': activeCategory === '' }"
          :to="{ path: '/' }"
        >全部商品</RouterLink>
        <RouterLink
          v-for="c in PRODUCT_CATEGORIES"
          :key="c"
          class="shop-catbar__item"
          :class="{ 'is-active': activeCategory === c }"
          :to="{ path: '/', query: { category: c } }"
        >{{ c }}</RouterLink>
      </nav>
    </div>
    <main class="shop-body">
      <RouterView />
    </main>
    <footer class="shop-footer">
      <span>Pulse · 订单履约中台演示站</span>
      <span>全部数据为模拟，不涉及真实交易 · 技术细节见「关于这个项目」</span>
    </footer>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter, RouterLink, RouterView } from 'vue-router'
import { ElAlert, ElInput, ElButton } from 'element-plus'
import { DEMO_BANNER } from '../router/routes'
import { PRODUCT_CATEGORIES } from '../utils/categories'
import { useCartStore } from '../stores/cart'

const route = useRoute()
const router = useRouter()
const cart = useCartStore()

const activeCategory = computed(() =>
  typeof route.query.category === 'string' && route.query.category !== 'all'
    ? route.query.category
    : '',
)

const search = ref('')
function submitSearch(): void {
  const q = search.value.trim()
  const query: Record<string, string> = {}
  if (q) query.q = q
  if (activeCategory.value) query.category = activeCategory.value
  router.push({ path: '/', query })
}
</script>

<style>
:root {
  --shop-bg: #f5f7fb;
  --shop-surface: #ffffff;
  --shop-ink: #172033;
  --shop-muted: #6b7a90;
  --shop-line: #e6ebf3;
  --shop-brand: #3b5bdb;
  --shop-brand-deep: #2f49b0;
  --shop-brand-soft: #eef2ff;
  --shop-accent: #f2711c;
  --shop-success: #1f9d6b;
  --shop-radius: 14px;
  --shop-shadow: 0 8px 28px rgba(31, 50, 90, 0.07);
}

.shop-page {
  max-width: 1180px;
  margin: 0 auto;
  padding: 28px 24px 56px;
}

.shop-hero {
  display: grid;
  grid-template-columns: minmax(0, 1.15fr) minmax(0, 0.85fr);
  gap: 28px;
  align-items: center;
  padding: 34px 36px;
  border-radius: 20px;
  background: linear-gradient(120deg, #1d2b64 0%, #3b5bdb 58%, #5572ec 100%);
  color: #fff;
  box-shadow: var(--shop-shadow);
}
.shop-hero__eyebrow {
  display: inline-block;
  padding: 4px 10px;
  border: 1px solid rgba(255, 255, 255, 0.4);
  border-radius: 999px;
  font-size: 12px;
  letter-spacing: 0.06em;
}
.shop-hero h1 {
  margin: 16px 0 10px;
  font-size: 34px;
  line-height: 1.25;
  font-weight: 650;
}
.shop-hero__desc {
  margin: 0;
  max-width: 560px;
  font-size: 14px;
  line-height: 1.85;
  color: rgba(255, 255, 255, 0.86);
}
.shop-hero__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 22px;
}
.shop-hero__actions .el-button {
  border-radius: 9px;
  font-weight: 500;
}
.shop-hero__diagram {
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.24);
  border-radius: 14px;
  padding: 18px;
}
.shop-hero__diagram-title {
  font-size: 12px;
  letter-spacing: 0.08em;
  color: rgba(255, 255, 255, 0.72);
  margin-bottom: 12px;
}
.shop-flow {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 7px;
}
.shop-flow__node {
  background: rgba(255, 255, 255, 0.94);
  color: var(--shop-brand-deep);
  border-radius: 7px;
  padding: 6px 9px;
  font-size: 12px;
  font-weight: 600;
}
.shop-flow__arrow { color: rgba(255, 255, 255, 0.7); font-size: 12px; }

.shop-section { margin-top: 30px; }
.shop-section__head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
}
.shop-section__title { margin: 0; font-size: 20px; font-weight: 620; color: var(--shop-ink); }
.shop-section__hint { font-size: 12px; color: var(--shop-muted); }

.shop-mw__grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 14px;
  margin-top: 14px;
}
.shop-mw__card {
  background: var(--shop-surface);
  border: 1px solid var(--shop-line);
  border-radius: var(--shop-radius);
  padding: 18px 20px;
  box-shadow: var(--shop-shadow);
}
.shop-mw__tag {
  display: inline-block;
  padding: 3px 9px;
  border-radius: 6px;
  background: var(--shop-brand-soft);
  color: var(--shop-brand-deep);
  font-size: 12px;
  font-weight: 650;
}
.shop-mw__card h3 { margin: 12px 0 8px; font-size: 15px; color: var(--shop-ink); }
.shop-mw__card p { margin: 0; font-size: 13px; line-height: 1.75; color: var(--shop-muted); }
.shop-mw__keys { margin-top: 10px; font-size: 11px; color: #94a3b8; font-family: ui-monospace, Menlo, monospace; }

.shop-evidence {
  margin-top: 26px;
  background: var(--shop-surface);
  border: 1px solid var(--shop-line);
  border-radius: var(--shop-radius);
  padding: 18px 20px;
  box-shadow: var(--shop-shadow);
}
.shop-evidence__head {
  display: flex; align-items: center; justify-content: space-between; gap: 12px;
  font-size: 14px; font-weight: 600; color: var(--shop-ink);
}
.shop-evidence__grid {
  display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-top: 14px;
}
.shop-evidence__item {
  border: 1px solid var(--shop-line); border-radius: 10px; padding: 12px 14px; background: #fbfcff;
}
.shop-evidence__label { font-size: 12px; color: var(--shop-muted); }
.shop-evidence__value { margin: 6px 0 4px; font-size: 22px; font-weight: 680; color: var(--shop-brand-deep); }
.shop-evidence__caption { font-size: 12px; color: var(--shop-muted); line-height: 1.6; }
.shop-empty {
  padding: 18px; border: 1px dashed var(--shop-line); border-radius: 10px;
  background: #fbfcff; color: var(--shop-muted); font-size: 13px; text-align: center;
}

.shop-toolbar { display: flex; flex-wrap: wrap; gap: 10px; }
.shop-result-count { font-size: 13px; color: var(--shop-muted); align-self: center; }

.product-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(252px, 1fr));
  gap: 16px;
}
.product-card {
  display: flex; flex-direction: column;
  background: var(--shop-surface); border: 1px solid var(--shop-line);
  border-radius: var(--shop-radius); overflow: hidden;
  box-shadow: var(--shop-shadow); transition: transform 0.18s ease, box-shadow 0.18s ease;
}
.product-card:hover { transform: translateY(-3px); box-shadow: 0 14px 32px rgba(31, 50, 90, 0.12); }
.product-card__body { padding: 14px 16px 16px; }
.product-card__name { font-size: 15px; font-weight: 600; color: var(--shop-ink); margin: 0 0 4px; }
.product-card__sku { font-size: 11px; color: #94a3b8; font-family: ui-monospace, Menlo, monospace; }
.product-card__meta { display: flex; align-items: center; justify-content: space-between; margin: 12px 0; }
.shop-price { color: var(--shop-accent); font-size: 20px; font-weight: 700; }
.shop-price small { font-size: 12px; font-weight: 500; }
.shop-stock { font-size: 12px; color: var(--shop-muted); }
.product-card__actions { display: flex; gap: 8px; }
.product-card__actions .el-button { flex: 1; margin: 0; border-radius: 8px; }

.shop-panel {
  background: var(--shop-surface); border: 1px solid var(--shop-line);
  border-radius: var(--shop-radius); padding: 20px 22px; box-shadow: var(--shop-shadow);
}
.shop-panel + .shop-panel { margin-top: 16px; }
.shop-panel__title { margin: 0 0 16px; font-size: 16px; font-weight: 620; color: var(--shop-ink); }
.shop-kv { display: flex; flex-wrap: wrap; gap: 8px 28px; font-size: 13px; }
.shop-kv__item { min-width: 150px; }
.shop-kv__k { color: var(--shop-muted); }
.shop-kv__v { color: var(--shop-ink); font-weight: 600; }
.shop-chip {
  display: inline-block; padding: 2px 8px; border-radius: 6px; font-size: 12px;
  background: var(--shop-brand-soft); color: var(--shop-brand-deep);
}
.shop-note { font-size: 12px; line-height: 1.8; color: var(--shop-muted); }
.shop-mono { font-family: ui-monospace, Menlo, monospace; font-size: 12px; }
.shop-row { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
.shop-spacer { flex: 1; }
.shop-total { font-size: 15px; color: var(--shop-ink); }
.shop-total strong { color: var(--shop-accent); font-size: 22px; }
.shop-summary { display: flex; justify-content: flex-end; align-items: center; gap: 18px; margin-top: 18px; }

@media (max-width: 860px) {
  .shop-hero { grid-template-columns: 1fr; padding: 26px 22px; }
  .shop-mw__grid, .shop-evidence__grid { grid-template-columns: 1fr; }
}
</style>

<style scoped>
.shop-layout { min-height: 100vh; background: var(--shop-bg); }
.shop-header {
  position: sticky; top: 0; z-index: 10;
  background: #fff; box-shadow: 0 1px 0 var(--shop-line);
}
.shop-topbar {
  display: flex; align-items: center; gap: 18px;
  padding: 0 28px; height: 60px;
}
.brand {
  display: flex; align-items: center; gap: 9px;
  font-size: 15px; font-weight: 650; color: var(--shop-ink);
  text-decoration: none; white-space: nowrap;
}
.brand-mark {
  display: inline-flex; align-items: center; justify-content: center;
  width: 28px; height: 28px; border-radius: 8px;
  background: var(--shop-brand); color: #fff; font-size: 15px; font-weight: 700;
}
.brand-sub { font-weight: 400; color: var(--shop-muted); font-size: 13px; }
.shop-search { flex: 1; max-width: 480px; }
.shop-search__input { width: 100%; }
.shop-topnav { display: flex; align-items: center; gap: 18px; }
.shop-topnav a {
  color: var(--shop-muted); text-decoration: none; font-size: 13px; white-space: nowrap;
}
.shop-topnav a:hover { color: var(--shop-brand-deep); }
.shop-topnav a.shop-topnav__tech {
  padding: 5px 11px;
  border-radius: 999px;
  background: var(--shop-brand-soft);
  color: var(--shop-brand-deep);
  font-weight: 600;
}
.shop-topnav a.shop-topnav__tech:hover { background: #dfe7ff; }
.cart-link {
  display: flex; align-items: center; gap: 7px;
  color: var(--shop-brand-deep); text-decoration: none; font-size: 13px; white-space: nowrap;
}
.cart-label { font-weight: 500; }
.cart-badge {
  min-width: 22px; height: 22px; padding: 0 6px; border-radius: 999px;
  display: inline-flex; align-items: center; justify-content: center;
  background: var(--shop-brand); color: #fff; font-size: 12px; font-weight: 600;
}
.shop-catbar {
  display: flex; flex-wrap: wrap; align-items: center; gap: 4px;
  padding: 9px 28px; border-top: 1px solid var(--shop-line);
}
.shop-catbar__item {
  padding: 5px 14px; border-radius: 999px; font-size: 13px;
  color: var(--shop-ink); text-decoration: none; white-space: nowrap;
  transition: background 0.15s ease, color 0.15s ease;
}
.shop-catbar__item:hover { background: var(--shop-brand-soft); color: var(--shop-brand-deep); }
.shop-catbar__item.is-active {
  background: var(--shop-brand); color: #fff; font-weight: 600;
}
.shop-body { min-height: calc(100vh - 160px); padding-bottom: 8px; }
.shop-footer {
  display: flex; flex-wrap: wrap; justify-content: space-between; gap: 8px;
  padding: 20px 28px; border-top: 1px solid var(--shop-line);
  color: #94a3b8; font-size: 12px; background: #fff;
}
@media (max-width: 700px) {
  .shop-topbar { padding: 0 14px; gap: 10px; }
  .brand-sub, .cart-label { display: none; }
  .shop-search { max-width: none; }
  .shop-topnav { gap: 10px; }
  .shop-topnav a { font-size: 12px; }
  .shop-topnav__optional { display: none; }
  .shop-catbar { padding: 8px 14px; }
}
</style>
