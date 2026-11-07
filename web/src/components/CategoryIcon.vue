<template>
  <!-- 零外部资源：五个分类各一组内联 SVG 图形，不加载任何图标字体或图片。 -->
  <svg
    class="category-icon"
    viewBox="0 0 48 48"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    role="img"
    :aria-label="`${category} 图标`"
  >
    <template v-if="category === '数码电器'">
      <path d="M12 26v-4a12 12 0 0 1 24 0v4" />
      <rect x="9" y="26" width="8" height="13" rx="3" />
      <rect x="31" y="26" width="8" height="13" rx="3" />
      <path d="M17 39c2 3 4 4 7 4" />
    </template>
    <template v-else-if="category === '家居生活'">
      <path d="M17 12h14l5 12H12l5-12Z" />
      <path d="M24 24v14" />
      <path d="M17 41h14" />
      <path d="M24 6v4" />
    </template>
    <template v-else-if="category === '服饰鞋包'">
      <path d="M18 8 12 12l-4 8 6 3v17h20V23l6-3-4-8-6-4" />
      <path d="M18 8c0 4 3 6 6 6s6-2 6-6" />
    </template>
    <template v-else-if="category === '美妆个护'">
      <rect x="16" y="17" width="16" height="23" rx="4" />
      <path d="M20 17v-5h8v5" />
      <path d="M20 27h8" />
      <circle cx="24" cy="33" r="2" />
    </template>
    <template v-else-if="category === '食品生鲜'">
      <path d="M24 42V14" />
      <path d="M24 20c-6 0-10-4-10-9 6 0 10 4 10 9Z" />
      <path d="M24 28c6 0 10-4 10-9-6 0-10 4-10 9Z" />
      <path d="M24 14c-4-3-4-7 0-10 4 3 4 7 0 10Z" />
    </template>
    <template v-else>
      <rect x="10" y="12" width="28" height="26" rx="4" />
      <path d="M16 22h16M16 29h10" />
    </template>
  </svg>
</template>

<script setup lang="ts">
defineProps<{
  category: string
}>()
</script>

<style scoped>
.category-icon { display: block; width: 100%; height: 100%; }
</style>
