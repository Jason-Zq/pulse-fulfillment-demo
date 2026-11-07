import type { RouteRecordRaw } from 'vue-router'

/** 全站演示声明（App.vue 与后台布局共用同一文案）。 */
export const DEMO_BANNER = '演示环境 · 全部数据为模拟 · 不涉及真实交易'

export interface NavItem {
  path: string
  title: string
}

/** 用户端全局导航（SPEC v0.2 18.3）：商城 / 我的订单 / 运维后台 / 关于本项目。 */
export const shopNav: NavItem[] = [
  { path: '/', title: '商城' },
  { path: '/orders', title: '我的订单' },
  { path: '/admin/dashboard', title: '运营后台' },
  { path: '/about', title: '关于这个项目' },
]

/** 后台侧栏导航：M4 五个核心页 + M5 的 dashboard / alerts / lab，共 8 项。 */
export const adminNav: NavItem[] = [
  { path: '/admin/dashboard', title: '仪表盘' },
  { path: '/admin/alerts', title: '告警中心' },
  { path: '/admin/lab', title: '实验台' },
  { path: '/admin/orders', title: '订单列表' },
  { path: '/admin/products', title: '商品与库存' },
  { path: '/admin/fulfillment', title: '发货与售后' },
  { path: '/admin/reports', title: '对账报表' },
  { path: '/admin/dead-letters', title: '死信补偿台' },
]

/**
 * 全站路由。页面级组件一律用 dynamic import()，既满足 Vite 路由级 code-split，
 * 也让 vitest 可在 node 环境把本文件当纯数据加载（不触发组件解析）。
 *
 * M6a 只接信息架构骨架：三个新商城页先落路由与占位组件，M6b 再替换为成熟页面；
 * 两套 Layout 组件（ShopLayout / AdminHeader）在 M6b/M6c 接线时统一接入。
 */
export const routes: RouteRecordRaw[] = [
  { path: '/', name: 'products', component: () => import('../views/Products.vue') },
  { path: '/product/:sku', name: 'product-detail', component: () => import('../views/ProductDetail.vue') },
  { path: '/cart', name: 'cart', component: () => import('../views/Cart.vue') },
  { path: '/checkout', name: 'checkout', component: () => import('../views/Checkout.vue') },
  { path: '/orders', name: 'orders', component: () => import('../views/Orders.vue') },
  { path: '/order/:id', name: 'order-detail', component: () => import('../views/OrderDetail.vue') },
  {
    path: '/order/:id/result',
    // M6a：旧的支付结果页改为兼容重定向，保留 :id 参数；M6b 起订单详情统一由 /order/:id 承载。
    redirect: (to) => ({ path: `/order/${String(to.params.id)}` }),
  },
  { path: '/about', name: 'about', component: () => import('../views/About.vue') },
  // M7：vue-rabbit 模板的 Login / Member 保留为「演示静态壳」，不接任何鉴权或用户系统。
  { path: '/member', name: 'member', component: () => import('../views/Member.vue') },
  { path: '/login', redirect: '/member' },
  {
    path: '/admin',
    component: () => import('../layouts/AdminLayout.vue'),
    children: [
      { path: '', redirect: '/admin/orders' },
      { path: 'dashboard', name: 'admin-dashboard', component: () => import('../views/admin/Dashboard.vue') },
      { path: 'alerts', name: 'admin-alerts', component: () => import('../views/admin/Alerts.vue') },
      { path: 'lab', name: 'admin-lab', component: () => import('../views/admin/Lab.vue') },
      { path: 'orders', name: 'admin-orders', component: () => import('../views/admin/Orders.vue') },
      { path: 'products', name: 'admin-products', component: () => import('../views/admin/Products.vue') },
      { path: 'fulfillment', name: 'admin-fulfillment', component: () => import('../views/admin/Fulfillment.vue') },
      { path: 'reports', name: 'admin-reports', component: () => import('../views/admin/Reports.vue') },
      { path: 'dead-letters', name: 'admin-dead-letters', component: () => import('../views/admin/DeadLetters.vue') },
    ],
  },
  { path: '/:pathMatch(.*)*', redirect: '/' },
]
