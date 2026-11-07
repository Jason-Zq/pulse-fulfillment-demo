<template>
  <!--
    M6b：`/order/:id/result` 已在 router/routes.ts 改为兼容重定向到 `/order/:id`。
    本文件仅保留旧链接的历史占位，不再渲染、不再自动调用 api.payOrder()。
    支付动作统一由订单详情页明确标注的「模拟支付 · 演示」按钮触发。
  -->
  <div class="shop-page">
    <div class="shop-empty">
      订单详情已迁移到「我的订单 → 详情」页面。
      <RouterLink to="/orders">前往我的订单</RouterLink>
    </div>
  </div>
</template>

<script setup lang="ts">
import { RouterLink } from 'vue-router'
</script>
