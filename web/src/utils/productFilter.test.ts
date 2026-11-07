import { describe, it, expect } from 'vitest'
import { filterProducts } from './productFilter'
import type { Product } from '../api/types'

function make(
  over: Partial<Product> & Pick<Product, 'sku' | 'name' | 'category'>,
): Product {
  return {
    id: 0,
    price: 100,
    available: 10,
    reserved: 0,
    deducted: 0,
    status: 'active',
    version: 0,
    ...over,
  } as Product
}

const sample: Product[] = [
  make({ sku: 'SKU-1', name: '无线降噪耳机', category: '数码电器', price: 299, available: 5 }),
  make({ sku: 'SKU-2', name: '北欧风落地灯', category: '家居生活', price: 159, available: 40 }),
  make({ sku: 'SKU-3', name: '纯棉基础T恤', category: '服饰鞋包', price: 79, available: 80 }),
]

describe('filterProducts', () => {
  it('category=all 且无关键字时返回全部', () => {
    expect(filterProducts(sample, { category: 'all' })).toHaveLength(3)
    expect(filterProducts(sample)).toHaveLength(3)
  })

  it('按分类筛选只返回该分类', () => {
    const r = filterProducts(sample, { category: '数码电器' })
    expect(r).toHaveLength(1)
    expect(r[0].sku).toBe('SKU-1')
  })

  it('空分类参数等价于不过滤', () => {
    expect(filterProducts(sample, { category: '' })).toHaveLength(3)
  })

  it('关键字匹配商品名（大小写不敏感、忽略首尾空白）', () => {
    expect(filterProducts(sample, { keyword: '  落地灯 ' })[0].sku).toBe('SKU-2')
    expect(filterProducts(sample, { keyword: 'T恤' })[0].sku).toBe('SKU-3')
  })

  it('关键字匹配 SKU', () => {
    expect(filterProducts(sample, { keyword: 'sku-3' })[0].sku).toBe('SKU-3')
  })

  it('分类与关键字组合取交集', () => {
    expect(filterProducts(sample, { category: '数码电器', keyword: '耳机' })).toHaveLength(1)
    expect(filterProducts(sample, { category: '家居生活', keyword: '耳机' })).toHaveLength(0)
  })

  it('按价格升序排序', () => {
    expect(filterProducts(sample, { sortBy: 'priceAsc' }).map((p) => p.price)).toEqual([79, 159, 299])
  })

  it('按价格降序排序', () => {
    expect(filterProducts(sample, { sortBy: 'priceDesc' }).map((p) => p.price)).toEqual([299, 159, 79])
  })

  it('按可售库存降序排序', () => {
    expect(filterProducts(sample, { sortBy: 'stockDesc' })[0].available).toBe(80)
  })

  it('排序不破坏原数组', () => {
    const before = sample.map((p) => p.sku)
    filterProducts(sample, { sortBy: 'priceAsc' })
    expect(sample.map((p) => p.sku)).toEqual(before)
  })
})
