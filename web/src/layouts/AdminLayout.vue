<template>
  <ElContainer class="admin-layout">
    <ElAside width="220px" class="admin-aside">
      <div class="brand">Pulse · 运营后台</div>
      <ElMenu :default-active="activePath" router>
        <ElMenuItem v-for="item in adminNav" :key="item.path" :index="item.path">
          {{ item.title }}
        </ElMenuItem>
      </ElMenu>
    </ElAside>
    <ElContainer>
      <!-- M6c：全局健康条接真实 /api/dashboard 的 source，不可用显示「—」。 -->
      <ElHeader class="admin-topbar">
        <div class="admin-topbar__title">{{ pageMeta.title }}</div>
        <AdminHeader />
      </ElHeader>
      <ElMain class="admin-main">
        <ElBreadcrumb separator="/" class="admin-crumbs">
          <ElBreadcrumbItem v-for="c in pageMeta.breadcrumb" :key="c">{{ c }}</ElBreadcrumbItem>
        </ElBreadcrumb>
        <div class="admin-hint">{{ pageMeta.description }}</div>
        <!-- 每页固定「本页技术证据」卡：元数据按 route.path 从 adminPageMeta 取，不写死某一页。 -->
        <TechEvidenceCard
          class="admin-evidence"
          :middlewares="pageMeta.middlewares"
          :keys="pageMeta.keys"
          :reproduce="pageMeta.reproduce"
        />
        <RouterView />
      </ElMain>
    </ElContainer>
  </ElContainer>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import {
  ElContainer, ElAside, ElMenu, ElMenuItem, ElHeader, ElMain,
  ElBreadcrumb, ElBreadcrumbItem,
} from 'element-plus'
import { adminNav } from '../router/routes'
import { adminPageMeta } from '../utils/adminEvidence'
import AdminHeader from './AdminHeader.vue'
import TechEvidenceCard from '../components/TechEvidenceCard.vue'

const route = useRoute()
const activePath = computed(() => route.path)
const pageMeta = computed(() => adminPageMeta(route.path))
</script>

<style scoped>
.admin-layout { min-height: 100vh; }
.admin-aside { background: #304156; color: #bfcbd9; }
.brand {
  padding: 20px 16px; font-weight: 600; color: #fff;
  border-bottom: 1px solid #3a4a5e;
}
.admin-aside :deep(.el-menu) { border-right: none; background: #304156; }
.admin-aside :deep(.el-menu-item) { color: #bfcbd9; }
.admin-aside :deep(.el-menu-item.is-active) { color: #409eff; background: #263445; }
.admin-topbar {
  display: flex; align-items: center; justify-content: space-between; gap: 16px;
  height: auto; min-height: 56px; padding: 0 20px;
  background: #fff; border-bottom: 1px solid #e4e7ed;
}
.admin-topbar__title { font-size: 17px; font-weight: 600; white-space: nowrap; }
.admin-topbar :deep(.admin-header) { flex: 1; border-bottom: none; }
.admin-main { padding: 16px 20px 28px; background: #f5f7fb; }
.admin-crumbs { font-size: 12px; }
.admin-hint { margin-top: 8px; color: #6b7a90; font-size: 12px; line-height: 1.7; }
.admin-evidence { margin: 12px 0 16px; }
.admin-evidence :deep(.row) { font-size: 12px; }
</style>
