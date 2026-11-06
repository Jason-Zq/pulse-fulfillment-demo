<template>
  <ElConfigProvider>
    <div class="app-shell">
      <ElAlert
        title="演示环境 · 全部数据为模拟 · 不涉及真实交易"
        type="warning"
        :closable="false"
        show-icon
      />
      <RouterView />
    </div>
  </ElConfigProvider>
</template>

<script setup lang="ts">
import { ElConfigProvider, ElAlert } from 'element-plus'
</script>

<style>
body {
  margin: 0;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
}
</style>
