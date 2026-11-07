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
    category: '数码电器',
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

describe('cart store — M7 行级勾选（仅结算已选）', () => {
  it('新加入的商品默认勾选，allSelected 为真', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.add(product('S2', 20, 3))
    expect(cart.hasSelection).toBe(true)
    expect(cart.allSelected).toBe(true)
    expect(cart.selectedLines).toHaveLength(2)
  })

  it('toggleSelect 可取消 / 恢复单行勾选', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.toggleSelect('S1')
    expect(cart.isSelected('S1')).toBe(false)
    expect(cart.selectedLines).toHaveLength(0)
    cart.toggleSelect('S1')
    expect(cart.isSelected('S1')).toBe(true)
  })

  it('toggleAll 在全选 / 全不选之间切换', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.add(product('S2', 20, 3))
    cart.toggleAll()
    expect(cart.hasSelection).toBe(false)
    expect(cart.allSelected).toBe(false)
    cart.toggleAll()
    expect(cart.allSelected).toBe(true)
  })

  it('selectedTotalAmount 只累计已勾选行', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5), 2)
    cart.add(product('S2', 20, 3), 1)
    expect(cart.selectedTotalAmount).toBe(40)
    expect(cart.selectedTotalCount).toBe(3)
    cart.toggleSelect('S1')
    expect(cart.selectedTotalAmount).toBe(20)
    expect(cart.selectedTotalCount).toBe(1)
    expect(cart.totalAmount).toBe(40)
  })

  it('selectedItems 只返回已勾选的 sku + quantity', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5), 2)
    cart.add(product('S2', 20, 3), 1)
    cart.toggleSelect('S2')
    expect(cart.selectedItems()).toEqual([{ sku: 'S1', quantity: 2 }])
  })

  it('remove / clear 会清理选中态，不留悬空 sku', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.add(product('S2', 20, 3))
    cart.remove('S1')
    expect(cart.selected).toEqual(['S2'])
    expect(cart.allSelected).toBe(true)
    cart.clear()
    expect(cart.selected).toEqual([])
    expect(cart.hasSelection).toBe(false)
  })

  it('setQuantity 归零移除该行时同步清理选中态', () => {
    const cart = useCartStore()
    cart.add(product('S1', 10, 5))
    cart.setQuantity('S1', 0)
    expect(cart.lines).toHaveLength(0)
    expect(cart.selected).toEqual([])
  })
})
