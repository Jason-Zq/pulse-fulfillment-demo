<template>
  <!-- 零外部资源：商品图由内联 SVG 生成，同一 SKU 永远得到同一张图。 -->
  <svg class="product-thumb" :viewBox="`0 0 320 180`" role="img" :aria-label="`${name || sku} 占位图`">
    <defs>
      <linearGradient :id="gradientId" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0%" :stop-color="from" />
        <stop offset="100%" :stop-color="to" />
      </linearGradient>
    </defs>
    <rect width="320" height="180" :fill="`url(#${gradientId})`" />
    <circle :cx="cx" cy="42" r="70" fill="rgba(255,255,255,0.13)" />
    <circle cx="270" cy="150" r="52" fill="rgba(255,255,255,0.10)" />
    <rect x="34" y="60" width="94" height="94" rx="16" fill="rgba(255,255,255,0.22)" />
    <rect x="52" y="78" width="58" height="58" rx="10" fill="rgba(255,255,255,0.42)" />
    <text x="150" y="92" fill="rgba(255,255,255,0.94)" font-size="26" font-weight="600">{{ initials }}</text>
    <text x="150" y="120" fill="rgba(255,255,255,0.72)" font-size="12" font-family="monospace">{{ sku }}</text>
    <text x="150" y="142" fill="rgba(255,255,255,0.58)" font-size="11">演示商品 · 无真实图片</text>
  </svg>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  sku: string
  name?: string
}>()

const PALETTE: Array<[string, string]> = [
  ['#5b6ee1', '#8e9df5'],
  ['#2f8f8a', '#6bc6b8'],
  ['#d97757', '#f1b28c'],
  ['#6b5bd2', '#a99ae8'],
  ['#2d6fb8', '#69a7e0'],
]

function hash(text: string): number {
  let h = 0
  for (let i = 0; i < text.length; i += 1) h = (h * 31 + text.charCodeAt(i)) >>> 0
  return h
}

const seed = computed(() => hash(props.sku || 'SKU'))
const pair = computed(() => PALETTE[seed.value % PALETTE.length])
const from = computed(() => pair.value[0])
const to = computed(() => pair.value[1])
const cx = computed(() => 40 + (seed.value % 70))
const gradientId = computed(() => `thumb-grad-${seed.value}`)
const initials = computed(() => (props.sku || 'SKU').slice(-2).toUpperCase())
</script>

<style scoped>
.product-thumb {
  display: block;
  width: 100%;
  height: auto;
  background: #eef2ff;
}
</style>
