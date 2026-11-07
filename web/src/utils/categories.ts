/**
 * 商品分类常量（M7 商城成熟化）。
 *
 * 与后端 Program.cs 种子严格对齐：后端 Product.Category 只会出现以下五个值之一
 * （外加空串，由 EnsureCreated 默认值兜底）。前端分类导航、筛选、面包屑都依赖本常量，
 * 单一数据源避免前后端分类漂移。
 */
export const PRODUCT_CATEGORIES = [
  '数码电器',
  '家居生活',
  '服饰鞋包',
  '美妆个护',
  '食品生鲜',
] as const

export type ProductCategory = (typeof PRODUCT_CATEGORIES)[number]

/** 路由查询参数里表示「不限分类」的占位值。 */
export const ALL_CATEGORY = 'all'
