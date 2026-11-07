<template>
  <!-- 零外部资源：4 个视角全部由同一 SKU + variant 生成的内联 SVG 组成。 -->
  <div class="gallery">
    <div class="gallery__main">
      <ProductThumb :sku="sku" :name="name" :variant="active" />
      <span class="gallery__counter">视角 {{ active + 1 }} / {{ VIEW_COUNT }}</span>
    </div>
    <div class="gallery__thumbs">
      <button
        v-for="v in VIEW_COUNT"
        :key="v"
        type="button"
        class="gallery__thumb"
        :class="{ 'is-active': active === v - 1 }"
        :aria-label="`切换到视角 ${v}`"
        :aria-pressed="active === v - 1"
        @click="active = v - 1"
      >
        <ProductThumb :sku="sku" :name="name" :variant="v - 1" />
      </button>
    </div>
    <div class="shop-note gallery__note">商品图为演示占位 SVG，零外部图片资源，不代表真实商品外观。</div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import ProductThumb from './ProductThumb.vue'

const props = defineProps<{
  sku: string
  name?: string
}>()

const VIEW_COUNT = 4
const active = ref(0)

// SKU 变化时回到第一个视角，避免从「同类推荐」跳到另一商品后停留在旧视角序号。
watch(() => props.sku, () => { active.value = 0 })
</script>

<style scoped>
.gallery__main { position: relative; border-radius: var(--shop-radius); overflow: hidden; }
.gallery__counter {
  position: absolute; right: 10px; bottom: 10px;
  padding: 3px 10px; border-radius: 999px;
  background: rgba(23, 32, 51, 0.62); color: #fff; font-size: 12px;
}
.gallery__thumbs { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin-top: 10px; }
.gallery__thumb {
  padding: 0; border: 2px solid transparent; border-radius: 8px;
  background: none; cursor: pointer; overflow: hidden; line-height: 0;
}
.gallery__thumb.is-active { border-color: var(--shop-brand); }
.gallery__note { margin-top: 10px; }
</style>
