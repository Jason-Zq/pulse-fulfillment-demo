<template>
  <ElCard class="tech-evidence">
    <template #header>本页技术证据</template>
    <div class="row">
      <span class="k">涉及的中间件</span>
      <ElTag v-for="m in middlewares" :key="m" size="small" type="info">{{ m }}</ElTag>
    </div>
    <div class="row">
      <span class="k">真实键 / 表 / 队列</span>
      <code v-for="k in keys" :key="k" class="key">{{ k }}</code>
    </div>
    <div v-if="reproduce" class="row">
      <span class="k">复现实验</span>
      <RouterLink :to="reproduce.path">
        <ElButton size="small">{{ reproduce.label }}</ElButton>
      </RouterLink>
    </div>
  </ElCard>
</template>

<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { ElCard, ElTag, ElButton } from 'element-plus'

interface ReproduceLink {
  label: string
  path: string
}

defineProps<{
  middlewares: string[]
  keys: string[]
  reproduce?: ReproduceLink
}>()
</script>

<style scoped>
.row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 5px 0;
  font-size: 13px;
}
.k { min-width: 140px; color: #909399; }
.key {
  padding: 2px 6px;
  background: #f4f4f5;
  border-radius: 4px;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
}
</style>
