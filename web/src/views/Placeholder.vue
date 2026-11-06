<template>
  <ElMain>
    <ElCard>
      <template #header>Pulse · 订单履约中台</template>
      <p>M0 骨架已就绪。后续里程碑将加入用户端下单页与 Element Plus 运营后台。</p>
      <ElButton type="primary" @click="checkHealth">检查后端健康状态</ElButton>
      <p v-if="health" class="health">{{ health }}</p>
    </ElCard>
  </ElMain>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { ElMain, ElCard, ElButton } from 'element-plus'

const health = ref('')

async function checkHealth(): Promise<void> {
  try {
    const res = await fetch('/api/health')
    health.value = res.ok ? '后端健康：ok' : `后端异常：HTTP ${res.status}`
  } catch {
    health.value = '后端未启动（请先运行 API 或 docker compose up）'
  }
}
</script>

<style scoped>
.health {
  margin-top: 12px;
  color: #606266;
}
</style>
