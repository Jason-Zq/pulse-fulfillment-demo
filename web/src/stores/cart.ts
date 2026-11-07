import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { PlaceOrderItem, Product } from '../api/types'

export interface CartLine {
  sku: string
  name: string
  price: number
  quantity: number
  available: number
}

export const useCartStore = defineStore('cart', () => {
  const lines = ref<CartLine[]>([])
  // M7 商城成熟化：行级勾选，仅结算已选中的商品。
  const selected = ref<string[]>([])

  function pruneSelection(): void {
    const skus = new Set(lines.value.map((l) => l.sku))
    if (selected.value.some((s) => !skus.has(s))) {
      selected.value = selected.value.filter((s) => skus.has(s))
    }
  }

  function add(product: Product, quantity = 1): void {
    if (quantity <= 0) return
    const max = Math.max(0, product.available)
    const existing = lines.value.find((l) => l.sku === product.sku)
    if (existing) {
      existing.quantity = Math.min(existing.quantity + quantity, max)
    } else {
      lines.value.push({
        sku: product.sku,
        name: product.name,
        price: product.price,
        quantity: Math.min(quantity, max),
        available: max,
      })
      if (!selected.value.includes(product.sku)) {
        selected.value = [...selected.value, product.sku]
      }
    }
  }

  function remove(sku: string): void {
    lines.value = lines.value.filter((l) => l.sku !== sku)
    pruneSelection()
  }

  function setQuantity(sku: string, quantity: number): void {
    if (quantity <= 0) {
      remove(sku)
      return
    }
    const line = lines.value.find((l) => l.sku === sku)
    if (line) line.quantity = Math.min(Math.floor(quantity), line.available)
  }

  function clear(): void {
    lines.value = []
    selected.value = []
  }

  function toggleSelect(sku: string): void {
    selected.value = selected.value.includes(sku)
      ? selected.value.filter((s) => s !== sku)
      : [...selected.value, sku]
  }

  function isSelected(sku: string): boolean {
    return selected.value.includes(sku)
  }

  function toggleAll(): void {
    selected.value = allSelected.value ? [] : lines.value.map((l) => l.sku)
  }

  const totalCount = computed(() => lines.value.reduce((sum, l) => sum + l.quantity, 0))
  const totalAmount = computed(() => lines.value.reduce((sum, l) => sum + l.quantity * l.price, 0))
  const isEmpty = computed(() => lines.value.length === 0)

  const selectedLines = computed(() => lines.value.filter((l) => selected.value.includes(l.sku)))
  const selectedTotalCount = computed(() => selectedLines.value.reduce((sum, l) => sum + l.quantity, 0))
  const selectedTotalAmount = computed(() =>
    selectedLines.value.reduce((sum, l) => sum + l.quantity * l.price, 0),
  )
  const allSelected = computed(
    () => lines.value.length > 0 && lines.value.every((l) => selected.value.includes(l.sku)),
  )
  const hasSelection = computed(() => selected.value.length > 0)

  function toOrderItems(): PlaceOrderItem[] {
    return lines.value.map((l) => ({ sku: l.sku, quantity: l.quantity }))
  }

  function selectedItems(): PlaceOrderItem[] {
    return selectedLines.value.map((l) => ({ sku: l.sku, quantity: l.quantity }))
  }

  return {
    lines,
    selected,
    add,
    remove,
    setQuantity,
    clear,
    toggleSelect,
    isSelected,
    toggleAll,
    totalCount,
    totalAmount,
    isEmpty,
    allSelected,
    hasSelection,
    selectedLines,
    selectedTotalCount,
    selectedTotalAmount,
    toOrderItems,
    selectedItems,
  }
})
