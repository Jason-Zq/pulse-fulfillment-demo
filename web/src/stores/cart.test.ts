import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useCartStore } from './cart'
import type { Product } from '../api/types'

function product(sku: string, price: number, available: number): Product {
  return {
    id: 1,
    sku,
    name: `商品${sku}`,
    price,
    available,
    reserved: 0,
    deducted: 0,
    status: 'OnSale',
  }
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('cart store — 购物车增删与合计', () => {
  it('初始为空', () => {
    const cart = useCartStore()
    expect(cart.lines).toEqual([])
    expect(cart.isEmpty).toBe(true)
    expect(cart.totalCount).toBe(0)
    expect(cart.totalAmount).toBe(0)
  })

  it('add 新增一行，同 SKU 累加数量', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.add(product('S1', 10, 5), 2)
    expect(cart.lines).toHaveLength(1)
    expect(cart.lines[0].quantity).toBe(3)
    expect(cart.totalCount).toBe(3)
    expect(cart.totalAmount).toBe(30)
  })

  it('不同 SKU 分多行，合计正确', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5), 2)
    cart.add(product('S2', 20, 3), 1)
    expect(cart.lines).toHaveLength(2)
    expect(cart.totalCount).toBe(3)
    expect(cart.totalAmount).toBe(40)
  })

  it('加入数量封顶到 available，不超过可售库存', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 2), 5)
    expect(cart.lines[0].quantity).toBe(2)
    cart.add(product('S1', 10, 2), 3)
    expect(cart.lines[0].quantity).toBe(2)
  })

  it('setQuantity 调整数量；<= 0 时移除该行', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.setQuantity('S1', 4)
    expect(cart.lines[0].quantity).toBe(4)
    cart.setQuantity('S1', 0)
    expect(cart.lines).toHaveLength(0)
  })

  it('setQuantity 同样封顶到 available', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.setQuantity('S1', 999)
    expect(cart.lines[0].quantity).toBe(5)
  })

  it('remove 与 clear', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.add(product('S2', 20, 3))
    cart.remove('S1')
    expect(cart.lines.map((l) => l.sku)).toEqual(['S2'])
    cart.clear()
    expect(cart.isEmpty).toBe(true)
  })

  it('toOrderItems 只返回 sku + quantity', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5), 2)
    expect(cart.toOrderItems()).toEqual([{ sku: 'S1', quantity: 2 }])
  })
})
