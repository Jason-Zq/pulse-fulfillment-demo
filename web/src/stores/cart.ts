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
    }
  }

  function remove(sku: string): void {
    lines.value = lines.value.filter((l) => l.sku !== sku)
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
  }

  const totalCount = computed(() => lines.value.reduce((sum, l) => sum + l.quantity, 0))
  const totalAmount = computed(() => lines.value.reduce((sum, l) => sum + l.quantity * l.price, 0))
  const isEmpty = computed(() => lines.value.length === 0)

  function toOrderItems(): PlaceOrderItem[] {
    return lines.value.map((l) => ({ sku: l.sku, quantity: l.quantity }))
  }

  return { lines, add, remove, setQuantity, clear, totalCount, totalAmount, isEmpty, toOrderItems }
})
