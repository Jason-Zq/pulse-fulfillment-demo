<template>
  <div class="shop-page">
    <ElAlert
      title="演示静态壳 · 本站没有真实用户系统，不提供注册与登录"
      type="warning" :closable="false" show-icon
    />

    <div class="shop-panel member-card">
      <div class="member-card__avatar">P</div>
      <div class="member-card__info">
        <h1 class="shop-section__title">演示访客</h1>
        <div class="shop-note">未登录状态 · 无真实账号 · 所有订单对访问者可见</div>
      </div>
      <span class="shop-chip">演示账号</span>
    </div>

    <div class="shop-panel">
      <h2 class="shop-panel__title">为什么这里没有登录？</h2>
      <ul class="member-notes">
        <li>参考模板 vue-rabbit 带有 Login / Member 会员体系；本项目定位为「零外部依赖的订单履约中台演示」，因此保留为静态壳，不接入模板的登录与 JWT 逻辑。</li>
        <li>没有用户表、没有 Session / JWT、没有鉴权中间件；GET /api/orders 对所有访问者可见，购物车只存在当前浏览器内存里。</li>
        <li>不收集姓名、手机号、收货地址等个人信息；结算页的地址是纯占位文本，不写入订单请求体。</li>
        <li>本页不发起任何后端请求，也没有 localStorage / Cookie 持久化。</li>
      </ul>
    </div>

    <div class="shop-panel">
      <h2 class="shop-panel__title">快捷入口</h2>
      <div class="shop-row">
        <RouterLink to="/orders"><ElButton type="primary" plain>我的订单</ElButton></RouterLink>
        <RouterLink to="/cart"><ElButton plain>购物车</ElButton></RouterLink>
        <RouterLink to="/"><ElButton plain>继续选购</ElButton></RouterLink>
        <RouterLink to="/admin/dashboard"><ElButton plain>运维后台</ElButton></RouterLink>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { ElAlert, ElButton } from 'element-plus'
</script>

<style scoped>
.member-card { display: flex; align-items: center; gap: 18px; margin-top: 16px; }
.member-card__avatar {
  display: flex; align-items: center; justify-content: center; flex: none;
  width: 64px; height: 64px; border-radius: 50%;
  background: var(--shop-brand-soft); color: var(--shop-brand-deep);
  font-size: 28px; font-weight: 700;
}
.member-card__info { flex: 1; min-width: 0; }
.member-notes { margin: 0; padding-left: 20px; font-size: 13px; line-height: 2; color: var(--shop-muted); }
</style>
