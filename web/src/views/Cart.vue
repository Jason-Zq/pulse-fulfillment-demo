<template>
  <div class="shop-page">
    <div class="shop-section__head">
      <h1 class="shop-section__title">购物车</h1>
      <ElButton text @click="router.push('/')">继续选购 →</ElButton>
    </div>

    <ElEmpty v-if="cart.isEmpty" description="购物车还是空的，去商城挑几件演示商品吧">
      <ElButton type="primary" @click="router.push('/')">去商城</ElButton>
    </ElEmpty>

    <template v-else>
      <div class="shop-panel cart-list">
        <div v-for="line in cart.lines" :key="line.sku" class="cart-line">
          <div class="cart-line__thumb"><ProductThumb :sku="line.sku" :name="line.name" /></div>
          <div class="cart-line__body">
            <div class="cart-line__name">{{ line.name }}</div>
            <div class="product-card__sku">{{ line.sku }} · 库存上限 {{ line.available }}</div>
            <div class="shop-note" style="margin-top: 6px">单价 ¥ {{ Number(line.price).toFixed(2) }}</div>
          </div>
          <div class="cart-line__qty">
            <ElButton size="small" @click="cart.setQuantity(line.sku, line.quantity - 1)">−</ElButton>
            <span class="cart-line__count">{{ line.quantity }}</span>
            <ElButton size="small" :disabled="line.quantity >= line.available" @click="cart.setQuantity(line.sku, line.quantity + 1)">＋</ElButton>
          </div>
          <div class="cart-line__subtotal">¥ {{ (line.price * line.quantity).toFixed(2) }}</div>
          <ElButton size="small" text type="danger" @click="cart.remove(line.sku)">移除</ElButton>
        </div>
      </div>

      <div class="shop-panel cart-summary">
        <div class="shop-note">
          共 {{ cart.lines.length }} 种商品 / {{ cart.totalCount }} 件。结算时会把全部明细一次写入同一个 MySQL 事务：
          多行 order_items、多行库存预扣与 Reserve 流水、一行 outbox。
        </div>
        <div class="shop-summary">
          <span class="shop-total">合计 <strong>¥ {{ cart.totalAmount.toFixed(2) }}</strong></span>
          <ElButton type="primary" size="large" @click="router.push('/checkout')">去结算</ElButton>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { ElButton, ElEmpty } from 'element-plus'
import { useCartStore } from '../stores/cart'
import ProductThumb from '../components/ProductThumb.vue'

const router = useRouter()
const cart = useCartStore()
</script>

<style scoped>
.cart-list { padding: 4px 22px; }
.cart-line {
  display: flex; align-items: center; gap: 18px;
  padding: 14px 0; border-bottom: 1px solid var(--shop-line);
}
.cart-line:last-child { border-bottom: none; }
.cart-line__thumb { width: 108px; flex: none; border-radius: 10px; overflow: hidden; }
.cart-line__body { flex: 1; min-width: 0; }
.cart-line__name { font-size: 15px; font-weight: 600; color: var(--shop-ink); }
.cart-line__qty { display: flex; align-items: center; gap: 8px; }
.cart-line__count { min-width: 28px; text-align: center; font-size: 14px; }
.cart-line__subtotal { min-width: 96px; text-align: right; font-size: 15px; font-weight: 650; color: var(--shop-accent); }
.cart-summary { margin-top: 16px; }
@media (max-width: 700px) {
  .cart-line { flex-wrap: wrap; }
  .cart-line__thumb { width: 80px; }
  .cart-line__subtotal { min-width: 0; }
}
</style>
