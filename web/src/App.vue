<template>
  <ElConfigProvider>
    <!--
      M6b：用户端统一套 ShopLayout（顶部导航 + 购物车角标 + 常驻演示声明），
      后台 /admin/* 仍走自己的 AdminLayout，只保留一个 RouterView 避免双层导航。
      App.vue 的全局 ElAlert 已移除——演示声明由 ShopLayout / AdminLayout 各自常驻。
    -->
    <ShopLayout v-if="!isAdmin" />
    <RouterView v-else />
  </ElConfigProvider>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, RouterView } from 'vue-router'
import { ElConfigProvider } from 'element-plus'
import ShopLayout from './layouts/ShopLayout.vue'

const route = useRoute()
const isAdmin = computed(() => route.path.startsWith('/admin'))
</script>

<style>
body {
  margin: 0;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'PingFang SC',
    'Microsoft YaHei', sans-serif;
  background: #f6f8fc;
  color: #1f2937;
}
</style>
