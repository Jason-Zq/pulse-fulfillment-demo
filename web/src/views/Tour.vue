<template>
  <div class="shop-page tour-page">
    <section class="shop-hero">
      <div>
        <span class="shop-hero__eyebrow">作品集演示 · 3 分钟导览</span>
        <h1>一笔订单，怎么跑通三个中间件</h1>
        <p class="shop-hero__desc">
          这页不是静态介绍，而是一条能自己复现的路线：按顺序看 7 个链路节点，每个计数都来自
          <span class="shop-mono">GET /api/pipeline</span> 的真实查询；复制下面的命令就能造一笔订单，
          再去后台逐项核对 MySQL / Redis / RabbitMQ 留下的痕迹。没有数据就显示「暂无实测数据」，不预填示例值。
        </p>
        <div class="shop-hero__actions">
          <RouterLink to="/"><ElButton type="warning">先去下单</ElButton></RouterLink>
          <RouterLink to="/admin/dashboard"><ElButton plain>看链路泳道</ElButton></RouterLink>
          <RouterLink to="/admin/lab"><ElButton plain>跑 E1 / E2 / E3</ElButton></RouterLink>
        </div>
      </div>
      <div class="shop-hero__diagram">
        <div class="shop-hero__diagram-title">读这页之前</div>
        <div class="shop-note" style="color: rgba(255,255,255,0.9); line-height: 2">
          ① {{ DEMO_BANNER }}<br />
          ② 浏览器不直连消息队列，页面状态靠 3 秒轮询与写后立即刷新<br />
          ③ 队列不可用显示「—」，绝不把不可用伪装成 0
        </div>
      </div>
    </section>

    <section class="shop-section">
      <div class="shop-section__head">
        <h2 class="shop-section__title">7 步链路 · 实时计数</h2>
        <span class="shop-section__hint">
          来自 GET /api/pipeline · 生成于 <span class="shop-mono">{{ generatedAtUtc }}</span>
          <ElButton link type="primary" @click="refresh">刷新</ElButton>
        </span>
      </div>
      <div v-if="nodes.length > 0" class="tour-steps">
        <article v-for="(node, i) in nodes" :key="node.key" class="tour-step">
          <div class="tour-step__index">{{ String(i + 1).padStart(2, '0') }}</div>
          <div class="tour-step__body">
            <div class="tour-step__head">
              <h3>{{ node.label }}</h3>
              <span class="tour-step__mw">{{ node.middleware }}</span>
              <span class="tour-step__count">
                {{ pipelineCountText(node) }} <small>{{ node.unit }}</small>
              </span>
            </div>
            <p class="tour-step__what">{{ stepDetail(node.key).what }}</p>
            <div class="tour-step__meta">
              <span class="shop-mono">真实来源：{{ node.source }}</span>
              <span class="shop-mono">键 / 表：{{ stepDetail(node.key).keys }}</span>
            </div>
            <div class="tour-step__links">
              <RouterLink :to="stepDetail(node.key).link" class="shop-chip">
                去{{ stepDetail(node.key).where }}核对
              </RouterLink>
              <span class="shop-note">{{ stepDetail(node.key).how }}</span>
            </div>
          </div>
        </article>
      </div>
      <div v-else class="shop-empty">
        链路计数暂不可用 · 不伪造 0；请确认六服务容器已启动后点「刷新」
      </div>
    </section>

    <section class="shop-panel">
      <h2 class="shop-panel__title">一键复现 · 复制即可在终端跑</h2>
      <p class="shop-note">
        端口 5080 是 API 直连；也可把域名换成演示站地址走 nginx。命令里的幂等键请每次换一个新值。
      </p>
      <div v-for="cmd in commands" :key="cmd.label" class="tour-cmd">
        <div class="tour-cmd__label">{{ cmd.label }}</div>
        <code class="tour-cmd__code">{{ cmd.text }}</code>
        <ElButton size="small" plain @click="copy(cmd.text)">复制</ElButton>
      </div>
    </section>

    <section class="shop-evidence">
      <div class="shop-evidence__head">
        <span>实测证据 · 来自 GET /api/experiments 的 experiment_results</span>
        <RouterLink to="/admin/lab" class="shop-chip">去实验台一键复跑</RouterLink>
      </div>
      <div v-if="highlights && highlights.length > 0" class="shop-evidence__grid">
        <div v-for="item in highlights" :key="item.name" class="shop-evidence__item">
          <div class="shop-evidence__label">{{ item.name }} · {{ item.label }}</div>
          <div class="shop-evidence__value">{{ item.value }}</div>
          <div class="shop-evidence__caption">{{ item.caption }}</div>
          <div class="shop-evidence__caption">跑于 {{ item.ranAtUtc }}</div>
        </div>
      </div>
      <div v-else class="shop-empty" style="margin-top: 14px">
        暂无实测数据 · 请先在实验台运行 E1 / E2 / E3，本页只展示真实结果，不预填任何数字
      </div>
    </section>

    <section class="shop-section">
      <div class="shop-section__head">
        <h2 class="shop-section__title">常见追问 · 点开看答案</h2>
        <span class="shop-section__hint">每条都指向可复现的真实证据，不是背八股</span>
      </div>
      <ElCollapse v-model="qaOpen" class="tour-qa">
        <ElCollapseItem v-for="qa in questions" :key="qa.q" :title="qa.q" :name="qa.q">
          <p class="shop-note">{{ qa.a }}</p>
        </ElCollapseItem>
      </ElCollapse>
    </section>

    <section class="shop-panel">
      <h2 class="shop-panel__title">边界（这不是生产系统）</h2>
      <p class="shop-note">
        全部数据为模拟、不做真实支付与物流、没有真实用户体系；公网部署卡在支付资质 / ICP 备案 / 高可用，
        且 .NET 8 已 EOL。完整清单与未验证项见仓库 <code>docs/BOUNDARIES.md</code> 与
        <RouterLink to="/about">关于这个项目</RouterLink>。
      </p>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { ElButton, ElCollapse, ElCollapseItem, ElMessage } from 'element-plus'
import { api } from '../api/client'
import type { PipelineSnapshot } from '../api/types'
import { DEMO_BANNER } from '../router/routes'
import { parseLatestEvidence } from '../utils/evidence'
import type { EvidenceHighlight } from '../utils/evidence'
import { orderedPipelineNodes, pipelineCountText } from '../utils/pipeline'

const pipeline = ref<PipelineSnapshot | null>(null)
const highlights = ref<EvidenceHighlight[] | null>(null)

/** 固定顺序由 utils/pipeline.ts 决定；后端没有的节点不渲染，不擅自补节点。 */
const nodes = computed(() => orderedPipelineNodes(pipeline.value?.nodes))
const generatedAtUtc = computed(() => pipeline.value?.generatedAtUtc ?? '—')

/** 每一步的「去哪看 / 看什么」是静态讲解，计数与来源一律取后端真实 DTO。 */
interface StepGuide {
  what: string
  keys: string
  link: string
  where: string
  how: string
}

const STEP_GUIDES: Record<string, StepGuide> = {
  orders: {
    what: '下单在同一事务写 orders / order_items / 库存预扣 / Reserve 流水 / outbox，乐观锁 version 挡住并发超卖。',
    keys: 'orders · order_items · inventory_ledgers',
    link: '/admin/orders',
    where: '订单列表',
    how: '看刚造出的订单与多明细，状态从 Created 开始。',
  },
  outbox: {
    what: 'Outbox Publisher 用 publisher confirms 把 pending 消息补投到 RabbitMQ 并回写状态，解决「库写成功但消息没发」。',
    keys: 'outbox_messages(status=pending)',
    link: '/admin/dashboard',
    where: '仪表盘泳道',
    how: 'E1 先停 Publisher 造单，恢复后看 pending 归零。',
  },
  exchange: {
    what: 'ex.orders(topic) 把 order.created / order.paid 路由到履约与超时队列，深度由 QueueDeclarePassive 真实读取。',
    keys: 'ex.orders · q.fulfillment · q.order.timeout',
    link: '/admin/dashboard',
    where: '仪表盘泳道',
    how: '队列拿不到时该节点返回 available=false，页面显示「—」。',
  },
  worker: {
    what: 'Worker 消费时先用 dedup:{event_id} SETNX 判重，再落 processed_events 唯一键，双保险防重复履约。',
    keys: 'dedup:* · processed_events',
    link: '/admin/dashboard',
    where: '仪表盘泳道',
    how: 'E2 直投大量重复消息，实扣流水仍只写一行。',
  },
  deduct: {
    what: '履约按 order_items 逐行实扣、写 Deduct 流水并把 reserved 归零；库存三态与流水是同一事实源。',
    keys: 'inventories · inventory_ledgers(ChangeType=Deduct)',
    link: '/admin/products',
    where: '商品与库存',
    how: '对比下单前后 available / reserved / deducted 三个数。',
  },
  deadLetter: {
    what: '三次重试仍失败的消息进 q.dlq.fulfillment 落 dead_letters，由补偿台用新 event_id 人工重投。',
    keys: 'q.dlq.fulfillment · dead_letters(status=pending)',
    link: '/admin/dead-letters',
    where: '死信补偿台',
    how: '用实验台的故障注入造一条失败消息，再点重投。',
  },
  report: {
    what: '日报即时聚合做订单 / 明细 / 流水 / 报表四方对账，差异应为 0；Quartz 每日 01:00 按 stat_date 幂等落表。',
    keys: 'daily_reports · inventory_ledgers',
    link: '/admin/reports',
    where: '对账报表',
    how: '跑完一笔订单后看 reconciliation.diff。',
  },
}

const FALLBACK_GUIDE: StepGuide = {
  what: '该节点的讲解待补充，计数仍以后端真实返回为准。',
  keys: '—',
  link: '/admin/dashboard',
  where: '仪表盘泳道',
  how: '计数不可用时显示「—」，不伪造 0。',
}

function stepDetail(key: string): StepGuide {
  return STEP_GUIDES[key] ?? FALLBACK_GUIDE
}

const commands = [
  {
    label: '① 下单：写 MySQL 事务 + outbox',
    text: `curl -X POST http://localhost:5080/api/orders -H 'Content-Type: application/json' -d '{"sku":"SKU-1","quantity":2,"idempotencyKey":"tour-001"}'`,
  },
  {
    label: '② 模拟支付：同事务写 order.paid outbox（演示，不涉及真实资金）',
    text: `curl -X POST http://localhost:5080/api/orders/1/pay -H 'Content-Type: application/json' -d '{}'`,
  },
  {
    label: '③ 看 7 节点真实计数',
    text: `curl http://localhost:5080/api/pipeline`,
  },
  {
    label: '④ 看订单详情与四方对账',
    text: `curl http://localhost:5080/api/orders/1 && curl 'http://localhost:5080/api/reports/daily'`,
  },
  {
    label: '⑤ 复跑实验（真实判据，结果落 experiment_results）',
    text: `curl -X POST http://localhost:5080/api/experiments/run -H 'Content-Type: application/json' -d '{"name":"E1"}'`,
  },
]

const questions = [
  {
    q: '为什么用事务性 Outbox，而不是下单后直接发 MQ？',
    a: '直接发会出现「库写成功但消息没发出去」或反过来。这里把 outbox 与订单写进同一事务，Publisher 用 publisher confirms 补投并回写状态。E1 先停 Publisher 造单、再恢复，能真看到 pending 全部补投且四方对账差异为 0。',
  },
  {
    q: '超时关单为什么用 TTL + DLX，而不是延迟消息插件？',
    a: '演示的超时时长固定 15 分钟，q.order.timeout 配 x-message-ttl=900000 与 DLX 即可，compose 少一个插件依赖。三次重试走 q.fulfillment.retry，最终进死信队列由人工补偿台重投。',
  },
  {
    q: 'Redis 判重之外为什么还要数据库唯一索引（双保险）？',
    a: 'idem: 与 dedup: 短钥匙用 SETNX 挡住重复点击与重复投递，但缓存过期或故障时必须有兜底：processed_events 的唯一键保证重复消费不会二次实扣。E2 直投大量同一 event_id 的消息，Deduct 流水仍只写一行。',
  },
  {
    q: '后台为什么用 3 秒轮询而不是 WebSocket / SignalR？',
    a: '后台是低频查看页，长连接会带来断线重连与多实例状态同步成本；演示站也不想引入新依赖。所有写操作后立即 refresh()，页面不会停留在旧状态。浏览器不直连消息队列。',
  },
  {
    q: '订单状态为什么要看 API 语义值，而不是直接查 MySQL 裸列？',
    a: 'Orders.Status 用 HasConversion<int>() 存整数，例如 2=Shipped、4=Cancelled；裸列是数字而非字符串。所有前端与验收脚本都以 API 返回的语义值为准，避免把存储细节当成业务契约。',
  },
  {
    q: 'E3 高并发为什么成功率低？是不是超卖了？',
    a: '独立探测显示失败请求全部是「库存并发冲突，请重试」（乐观锁 Inventory.Version），没有一个是库存不足；超卖数为 0。这是有意取舍：不自动重试冲突，换来零超卖，代价是高并发下成功率低。吞吐与 P99 每次跑都会波动，页面只展示 experiment_results 里的最新真实值。',
  },
  {
    q: '队列深度不可用时为什么显示「—」而不是 0？',
    a: 'QueueDeclarePassive 拿不到深度时后端返回 available=false 且 count=-1。把不可用伪装成 0 会让人误判「队列已清空」，所以前端统一显示「—」，并在 /api/dashboard 的 source 里如实反映依赖状态。',
  },
  {
    q: '.NET 8 已 EOL，为什么演示站还用它？',
    a: '技术栈由项目约束固定，本地运行、CI 与演示不受影响；但正式对外经营前必须升级到受支持的 LTS，并补齐支付资质、ICP 备案、高可用与个人信息保护。完整风险清单见 docs/BOUNDARIES.md，本文不把演示站说成生产系统。',
  },
]

const qaOpen = ref<string[]>([])

async function load(): Promise<void> {
  // 两个数据源独立：任一失败都保留空态，绝不补假数字。
  try {
    pipeline.value = await api.getPipeline()
  } catch {
    pipeline.value = null
  }
  try {
    highlights.value = parseLatestEvidence(await api.listExperiments())
  } catch {
    highlights.value = null
  }
}

function refresh(): void {
  void load()
}

async function copy(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制到剪贴板')
  } catch {
    ElMessage.error('复制失败，请手动选中命令文本')
  }
}

onMounted(() => {
  void load()
})
</script>

<style scoped>
.tour-steps { display: grid; gap: 14px; margin-top: 18px; }
.tour-step {
  display: grid;
  grid-template-columns: 54px minmax(0, 1fr);
  gap: 16px;
  padding: 18px 20px;
  border: 1px solid var(--shop-line);
  border-radius: var(--shop-radius);
  background: var(--shop-surface);
  box-shadow: var(--shop-shadow);
}
.tour-step__index {
  font-size: 22px;
  font-weight: 700;
  color: var(--shop-brand);
  font-family: ui-monospace, Menlo, monospace;
}
.tour-step__head { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
.tour-step__head h3 { margin: 0; font-size: 16px; color: var(--shop-ink); }
.tour-step__mw {
  padding: 2px 9px;
  border-radius: 999px;
  background: var(--shop-brand-soft);
  color: var(--shop-brand-deep);
  font-size: 11px;
}
.tour-step__count {
  margin-left: auto;
  font-size: 22px;
  font-weight: 700;
  color: var(--shop-brand-deep);
}
.tour-step__count small { font-size: 12px; font-weight: 500; color: var(--shop-muted); }
.tour-step__what { margin: 10px 0; font-size: 13px; line-height: 1.8; color: var(--shop-muted); }
.tour-step__meta { display: flex; flex-wrap: wrap; gap: 6px 24px; color: #94a3b8; }
.tour-step__links {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-top: 12px;
}
.tour-cmd {
  display: grid;
  grid-template-columns: minmax(180px, 260px) minmax(0, 1fr) auto;
  gap: 12px;
  align-items: center;
  padding: 10px 0;
  border-bottom: 1px dashed var(--shop-line);
}
.tour-cmd:last-child { border-bottom: 0; }
.tour-cmd__label { font-size: 12px; color: var(--shop-muted); }
.tour-cmd__code {
  display: block;
  overflow-x: auto;
  padding: 8px 10px;
  border-radius: 8px;
  background: #f2f5fb;
  color: #2f49b0;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11px;
  white-space: nowrap;
}
.tour-qa :deep(.el-collapse-item__header) { font-weight: 600; color: var(--shop-ink); }
@media (max-width: 760px) {
  .tour-step { grid-template-columns: 36px minmax(0, 1fr); padding: 14px; }
  .tour-cmd { grid-template-columns: 1fr; }
  .tour-step__count { margin-left: 0; }
}
</style>
