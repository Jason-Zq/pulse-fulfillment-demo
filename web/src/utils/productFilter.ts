import type { Product } from '../api/types'
import { ALL_CATEGORY } from './categories'

export type ProductSort = 'default' | 'priceAsc' | 'priceDesc' | 'stockDesc'

export interface ProductFilter {
  /** 名称 / SKU 模糊匹配；空串表示不按关键字过滤。 */
  keyword?: string
  /** 分类名；ALL_CATEGORY 或不传表示不限。 */
  category?: string
  sortBy?: ProductSort
}

/**
 * 纯函数：按关键字、分类、排序筛选商品列表。
 *
 * - 关键字同时匹配 name 与 sku（大小写不敏感、首尾空白忽略）。
 * - category 为 ALL_CATEGORY 或空时不过滤分类。
 * - 排序均为非破坏式（返回新数组），不影响入参。
 *
 * 同时被首页内联工具栏、顶栏全局搜索与分类导航复用，便于单测覆盖。
 */
export function filterProducts(products: Product[], filter: ProductFilter = {}): Product[] {
  const q = (filter.keyword ?? '').trim().toLowerCase()
  const cat = filter.category && filter.category !== ALL_CATEGORY ? filter.category : ''
  const sortBy = filter.sortBy ?? 'default'

  const matched = products.filter((p) => {
    const matchKeyword =
      !q || p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q)
    const matchCategory = !cat || p.category === cat
    return matchKeyword && matchCategory
  })

  if (sortBy === 'priceAsc') {
    return [...matched].sort((a, b) => Number(a.price) - Number(b.price))
  }
  if (sortBy === 'priceDesc') {
    return [...matched].sort((a, b) => Number(b.price) - Number(a.price))
  }
  if (sortBy === 'stockDesc') {
    return [...matched].sort((a, b) => b.available - a.available)
  }
  return matched
}
