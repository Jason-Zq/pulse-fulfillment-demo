<template>
  <div>
    <ElCard>
      <template #header>站内日报与四方对账</template>
      <ElForm :inline="true">
        <ElFormItem label="统计日期（UTC）">
          <ElInput v-model="date" placeholder="yyyy-MM-dd" style="width: 180px" />
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" @click="load">查询</ElButton>
        </ElFormItem>
      </ElForm>
      <ElEmpty
        v-if="report.orderCnt === 0 && report.gmv === 0"
        description="该日期暂无订单数据 · 可换日期查询或先在商城下单"
        :image-size="70"
      />
      <ElAlert
        title="GET /api/reports/daily 为即时聚合（四方对账实时计算）；persisted 标记该日是否已由 Quartz 定时任务落 daily_reports 表。定时任务每天 01:00（Asia/Shanghai）生成昨日日报，重跑按 stat_date 幂等 upsert。"
        type="info"
        :closable="false"
        class="mt"
      />
      <ElDescriptions :column="2" border class="mt">
        <ElDescriptionsItem label="统计日期">{{ report.statDate || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem label="订单数">{{ report.orderCnt }}</ElDescriptionsItem>
        <ElDescriptionsItem label="支付数">{{ report.paidCnt }}</ElDescriptionsItem>
        <ElDescriptionsItem label="取消数">{{ report.cancelCnt }}</ElDescriptionsItem>
        <ElDescriptionsItem label="GMV">¥ {{ formatAmount(report.gmv) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="对账差异 diff">
          <ElTag :type="report.reconciliation.diff === 0 ? 'success' : 'danger'">
            {{ report.reconciliation.diff }}
          </ElTag>
        </ElDescriptionsItem>
      </ElDescriptions>
      <h4>四方对账</h4>
      <ElDescriptions :column="1" border>
        <ElDescriptionsItem label="① 订单金额合计">¥ {{ formatAmount(report.reconciliation.orderAmount) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="② 明细金额合计">¥ {{ formatAmount(report.reconciliation.itemAmount) }}</ElDescriptionsItem>
        <ElDescriptionsItem label="③ Reserve 流水数量">{{ report.reconciliation.ledgerReserveQty }}</ElDescriptionsItem>
        <ElDescriptionsItem label="④ 差异 diff（应为 0）">{{ report.reconciliation.diff }}</ElDescriptionsItem>
      </ElDescriptions>
    </ElCard>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElButton,
  ElAlert,
  ElDescriptions,
  ElDescriptionsItem,
  ElTag,
  ElEmpty,
  ElMessage,
} from 'element-plus'
import { api } from '../../api/client'
import type { DailyReport } from '../../api/types'
import { alignDailyReport, formatAmount } from '../../utils/metrics'

const date = ref(new Date().toISOString().slice(0, 10))
const report = ref<DailyReport>(alignDailyReport(null))

async function load(): Promise<void> {
  try {
    report.value = alignDailyReport(await api.getDailyReport(date.value))
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '日报加载失败')
  }
}

onMounted(load)
</script>

<style scoped>
.mt {
  margin-top: 16px;
}
h4 {
  margin: 20px 0 8px;
}
</style>
