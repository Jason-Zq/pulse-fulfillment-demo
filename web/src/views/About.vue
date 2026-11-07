<template>
  <div class="shop-page">
    <section class="shop-hero">
      <div>
        <span class="shop-hero__eyebrow">关于这个项目</span>
        <h1>Pulse · 订单履约中台</h1>
        <p class="shop-hero__desc">
          一个可公网讲解、可逐层追问的作品集演示站：一条完整的订单履约链路，
          把 MySQL / Redis / RabbitMQ 各自解决的真实问题讲透，并给出跑得出来的量化证据。
          本页所有数字都来自实验台真实结果，没有数据就显示「暂无实测数据」。
        </p>
        <div class="shop-hero__actions">
          <RouterLink to="/"><ElButton type="warning">去下单</ElButton></RouterLink>
          <RouterLink to="/admin/dashboard"><ElButton plain>进运维后台</ElButton></RouterLink>
          <RouterLink to="/admin/lab"><ElButton plain>看实测数据</ElButton></RouterLink>
        </div>
      </div>
      <div class="shop-hero__diagram">
        <div class="shop-hero__diagram-title">阅读路径</div>
        <div class="shop-note" style="color: rgba(255,255,255,0.9); line-height: 2">
          ① 30 秒：商城首屏看懂系统定位与三个中间件<br />
          ② 5 分钟：运维后台看一笔订单从事务到对账的全链路<br />
          ③ 深挖：实验台一键复跑 E1 / E2 / E3，读真实 JSON 结果
        </div>
      </div>
    </section>

    <section class="shop-section">
      <div class="shop-section__head">
        <h2 class="shop-section__title">三个中间件，各解决一个真实问题</h2>
        <span class="shop-section__hint">卡片只描述实验设计与断言，不写死任何结果数字</span>
      </div>
      <div class="shop-mw__grid">
        <div v-for="item in middlewareCards" :key="item.name" class="shop-mw__card">
          <span class="shop-mw__tag">{{ item.name }}</span>
          <h3>{{ item.problem }}</h3>
          <p>{{ item.design }}</p>
          <div class="shop-mw__keys">{{ item.keys }}</div>
        </div>
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
        暂无实测数据 · 可到实验台一键复跑 E1 / E2 / E3，结果会自动出现在这里
      </div>
    </section>

    <section class="shop-section shop-panel">
      <h2 class="shop-panel__title">边界说明（这不是生产系统）</h2>
      <ul class="shop-boundary">
        <li>全部数据为模拟，不涉及真实交易；不收集真实个人信息，不设注册。</li>
        <li>不做真实支付 / 收银台 / 第三方对接；「模拟支付 · 演示」按钮只是异步履约链路的入口。</li>
        <li>未实现完整认证授权、高可用与灾备、生产级审计与合规；.NET 8 已 EOL（演示不受影响）。</li>
        <li>Quartz 采用 RAMJobStore，未落地 ADO.NET JobStore / qrtz_* 官方表；日报按 stat_date 幂等 upsert。</li>
        <li>完整清单、上线三档结论与未验证项见仓库 <code>docs/BOUNDARIES.md</code>。</li>
      </ul>
    </section>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { ElButton } from 'element-plus'
import { api } from '../api/client'
import { parseLatestEvidence } from '../utils/evidence'
import type { EvidenceHighlight } from '../utils/evidence'

const highlights = ref<EvidenceHighlight[] | null>(null)

onMounted(async () => {
  try {
    highlights.value = parseLatestEvidence(await api.listExperiments())
  } catch {
    // 实验台不可用时保留 null，页面显示「暂无实测数据」，不伪造数字。
    highlights.value = null
  }
})

/** 只写「实验怎么设计、断言什么」，结果数字一律由上面的真实实验记录提供。 */
const middlewareCards = [
  {
    name: 'MySQL',
    problem: '事实源：同事务落库',
    design: '订单、明细、库存预扣、流水与 outbox 在同一事务写入；日报做四方对账，断言差异为 0。',
    keys: 'orders / order_items / inventory_ledgers / outbox',
  },
  {
    name: 'Redis',
    problem: '幂等：重复请求只生效一次',
    design: 'idem: / dedup: 短钥匙用 SETNX 挡住重复点击与重复投递，与数据库唯一索引构成双保险。',
    keys: 'idem:* / dedup:* / alert:seen:* / processed_events',
  },
  {
    name: 'RabbitMQ',
    problem: '异步：履约、超时与补偿',
    design: 'outbox → ex.orders → 履约 / 超时 / 重试 / 死信；publisher confirms 与手动 ack 保证不静默丢消息。',
    keys: 'q.fulfillment / q.fulfillment.retry / q.dlq.fulfillment',
  },
]
</script>

<style scoped>
.shop-boundary { margin: 0; padding-left: 20px; color: var(--shop-muted); font-size: 13px; line-height: 2; }
code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
</style>
