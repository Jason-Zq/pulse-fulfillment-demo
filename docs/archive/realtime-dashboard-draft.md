# Pulse — 实时事件处理与运营看板 · 设计文档

- 状态：**待用户确认**（Superpowers 头脑风暴阶段产出）
- 日期：2026-11-06
- 项目性质：个人作品集，全栈
- 一句话：一个能一键启动、能自己造流量、能压测、能故障注入的实时事件管道，企业微信只是旁路的通知出口。

## 0. 本文档的假设

1. 方向默认采用上一轮讨论的首推方案「实时事件处理管道 + 可视化大屏」。若改选秒杀 / 任务调度，本文档作废重做。
2. "后端用 net" 理解为 **C# / ASP.NET Core on .NET**；"前端用 vue" 理解为 **Vue 3 + TypeScript + Vite**。
3. 用户未明确企业微信权限，通知渠道默认走可离线运行的 Mock，接口预留 Webhook 与自建应用实现。
4. 用户未明确是否要在线 demo，先以本地 `docker compose` 为唯一交付路径。

## 1. 定位与验收标准

作品集的评判标准是「可演示、可被追问、有量化证据」，不是业务实用性。因此验收项全部可验证：

| 编号 | 验收项 | 判据 |
|---|---|---|
| A1 | 一键启动 | `docker compose up -d` 后 2 分钟内大屏可访问，健康检查全绿 |
| A2 | 可见地跑起来 | 点击「造流量」，大屏指标每秒跳动，事件流持续追加 |
| A3 | 数据可信 | 任意时刻 Redis 实时计数与 MySQL 明细聚合对账，计数一致 |
| A4 | 不丢不重 | 重复投递 1000 条相同事件，聚合计数不增加；MQ 停机后重启不丢消息 |
| A5 | 故障可恢复 | 停消费者 → 队列积压曲线上升 → 恢复后追平，无消息进入死信 |
| A6 | 量化证据 | README 给出吞吐、端到端 P99、消息丢失率、恢复耗时的实测表 |
| A7 | 旁路不拖累主链路 | 企业微信不可用时，事件处理与主链路完全不受影响 |
| A8 | 工程品质 | 核心链路先有失败测试再有实现；`dotnet test` 与 `vitest run` 全绿 |

**明确不做**：多租户、完整登录权限体系（仅一个演示账号）、真实支付、Kafka / Elasticsearch / 微服务拆分、数据归档策略。复杂度冒头就砍。

## 2. 技术栈

| 层 | 选型 | 版本与理由 |
|---|---|---|
| 后端运行时 | ASP.NET Core on **.NET 10** | 当前 LTS，支持至 2028-11-14。**.NET 8 / 9 于 2026-11-10 EOL，不选** |
| ORM / DB | EF Core 10 + Pomelo.EntityFrameworkCore.MySql + MySQL 8.x | 事务性发件箱与日聚合都依赖关系型事务 |
| 消息队列 | RabbitMQ.Client（当前稳定版）+ RabbitMQ 3.13+ management | 需要重试 / 死信 / 确认语义，不是日志重放 |
| 缓存 | StackExchange.Redis（当前稳定版）+ Redis 7.x | 实时窗口、幂等短钥匙、健康快照 |
| 实时推送 | **SignalR**（ASP.NET Core 内置）+ `@microsoft/signalr` | 见决策 D1 |
| 定时任务 | Quartz.NET + ADO.NET JobStore（持久化到 MySQL） | 见决策 D4 |
| 可观测 | Serilog + prometheus-net，可选 Grafana | 队列深度、消费延迟、P99 |
| 后端测试 | xUnit + Testcontainers.NET | 用真容器跑集成测试，不 mock 掉中间件 |
| 前端 | **Vue 3.5.x + TypeScript + Vite 8** | 3.6 仍为 RC，锁稳定线；Node 22+ / 24 LTS |
| 状态 / 路由 | Pinia 4.x + Vue Router 5.x | |
| 图表 | ECharts（当前稳定版） | 实时折线、分布、仪表盘 |
| 前端测试 | Vitest | 指标窗口裁剪、SignalR 消息 reducer |
| 编排 | Docker Compose | mysql / redis / rabbitmq / api / web |

版本在实施前用 `dotnet --list-sdks` 与 `npm view vue version` 复核一次，以复核结果为准。

## 3. 架构与数据流

```
[流量生成器 / 埋点 HTTP] → [接入 API] → MySQL(outbox + events, 单事务)
                                      ↓ OutboxPublisher (publisher confirms)
                                 RabbitMQ: ex.events (topic)
                                      ↓ q.ingest (手动 ack, prefetch 限流)
                                 [聚合 Worker]
                                      ├→ Redis: 秒级分桶计数 / 幂等键 / 健康快照
                                      ├→ MySQL: events 明细 + event_daily_agg 日聚合
                                      └→ SignalR Hub (每秒一次快照，非逐事件推送)
                                                ↓
                                          Vue 实时大屏 (ECharts)
                                      ↓ 失败
                              q.ingest.retry (TTL) → q.ingest.dlx (死信 + 告警)
                                      ↓ 旁路（只订阅结果）
                     [告警规则引擎 / Quartz 日报] → INotificationChannel → 企业微信
```

六个组件的职责边界（每个都对应一个可被追问的问题）：

| 组件 | 不可替代理由 | 面试官会问什么 |
|---|---|---|
| MySQL | 事实源：明细、日聚合、发件箱、通知日志，要能审计与对账 | DB 写成功但 MQ 没发出去怎么办 |
| Redis | 秒级实时窗口与幂等短钥匙，MySQL 扛不住逐事件计数 | Redis 挂了/数据丢了会怎样（答：可重建） |
| RabbitMQ | 削峰、重试、死信、至少一次投递语义 | 重复消费、消息积压、顺序性怎么处理 |
| SignalR | 服务端主动推，避免前端轮询打爆 API | 断线重连后数据怎么补齐 |
| Quartz.NET | 长期、可编辑的周期规则（每日日报）与告警评估 | 多实例会不会重复发日报 |
| 企业微信 | 只是通知出口 | 挂了会不会影响主链路 |

## 4. 关键设计决策

**D1 用 SignalR，不用 SSE / 轮询。** .NET 生态里 SignalR 是官方一等公民：强类型 Hub、自动重连、连接分组、传输降级（WebSocket → 长轮询）、官方 TS 客户端。手写 SSE 要自己处理心跳、断线补偿与背压，代码更多但讲不出更多东西。替代方案 SSE 保留在文档里作为对比说明，不实现。

**D2 MySQL 是事实源，Redis 只是可重建的加速层。** 所有可追溯数据落 MySQL；Redis 里的窗口计数、幂等键、健康快照丢了就从 MySQL 重放聚合。这条边界让「Redis 宕机」这个追问有干净答案。

**D3 引入事务性发件箱（outbox）。** 接入 API 在一个 MySQL 事务里同时写 `events` 与 `outbox`，由后台 `OutboxPublisher` 批量读取、开 publisher confirms 投递、成功后标记 `published_at`。这解决了「DB 写成功但 MQ 发失败」的经典问题，配合消费端幂等形成端到端不丢不重的闭环——这是 RabbitMQ 部分最值得讲的点，也是 A4 判据的基础。取舍：多一次轮询、端到端延迟增加约 100ms，作品集场景完全可接受。

**D4 定时用 Quartz.NET，不用 MQ 延迟消息。** 每日日报是长期、可编辑的周期规则；TTL+DLX 或延迟插件对长延迟与规则变更都不顺手。Quartz 用 ADO.NET JobStore 持久化到 MySQL，避免多实例重复触发；cron 明确指定 `Asia/Shanghai`。MQ 延迟消息在本项目里**只用于短周期的重试退避**（见第 6 节），职责不越界。

**D5 RabbitMQ 而非 Kafka。** 本项目语义是「任务队列 + 逐条确认 + 重试 + 死信 + 削峰」，不是日志重放与多消费者订阅同一分区。RabbitMQ 运维成本也低，符合一键启动的交付要求。

**D6 时间统一。** 所有时间以 UTC `DATETIME(3)` 存 MySQL，前端按 `Asia/Shanghai` 展示，Quartz cron 显式带时区。避免跨日聚合错位这个高频低级 bug。

**D7 通知渠道可插拔。** `INotificationChannel` 一个接口，三个实现：`MockChannel`（默认，写日志 + 大屏可见）、`WebhookChannel`（群机器人，一个 URL 即可跑通、无需企业资质）、`WecomAppChannel`（预留，自建应用按 userid 发卡片）。配置 `Notification:Channel` 切换，保证没有企业微信权限也能完整演示整个项目。

## 5. 数据模型（MySQL）

| 表 | 关键字段 | 约束 / 索引 |
|---|---|---|
| `events` | `id` BIGINT PK, `event_id` CHAR(36), `type`, `source`, `occurred_at`, `payload` JSON, `ingested_at` | **`UNIQUE(event_id)` 幂等最终保障**；`INDEX(type, occurred_at)` |
| `outbox` | `id`, `event_id`, `routing_key`, `payload` JSON, `status` TINYINT, `retry_count`, `created_at`, `published_at` | `INDEX(status, created_at)`；与 `events` 同事务写入 |
| `event_daily_agg` | `stat_date` DATE, `type`, `cnt` BIGINT, `amount_sum` DECIMAL(18,2), `updated_at` | `UNIQUE(stat_date, type)`，用 `INSERT ... ON DUPLICATE KEY UPDATE` 累加 |
| `alert_rules` | `id`, `name`, `metric`, `op`, `threshold`, `window_sec`, `silence_sec`, `channel`, `enabled` | 最小 CRUD，不做复杂表达式 |
| `alert_records` | `id`, `rule_id`, `fired_at`, `value`, `status`, `error` | `INDEX(rule_id, fired_at)` |
| `notification_log` | `id`, `biz_key` VARCHAR(128), `channel`, `payload`, `sent_at`, `status`, `retry_count`, `error` | **`UNIQUE(biz_key)`**，日报/告警发送幂等 |
| `qrtz_*` | Quartz 官方表结构 | 官方脚本生成，不手改 |

Redis key 约定：
- 秒级分桶 `m:qps:{type}:{yyyyMMddHHmmss}`，`INCR` + TTL 120s，取最近 60 桶求和得滑窗 QPS。
- 幂等短钥匙 `dedup:{event_id}`，`SETNX` + TTL 24h；**MySQL 唯一索引才是最终保障**。
- 告警静默 `alert:silence:{rule_id}`，`SETNX` + TTL = `silence_sec`。
- 健康快照 `health:snapshot`，供大屏与告警评估读取。
- 日报幂等键 `daily:{yyyy-MM-dd}` 落 `notification_log.biz_key`。

## 6. 消息拓扑（RabbitMQ）

```
ex.events (topic, durable)
  └─ q.ingest (durable)  ← binding: event.*
       ├─ 成功 → 手动 basic.ack
       └─ 失败 → q.ingest.retry (TTL 5s, DLX 回 ex.events.retry)
                    └─ retry_count >= 3 → q.ingest.dlx (死信, 记 alert_records)
ex.events.retry (topic, durable)  ← 重试队列满 TTL 后回投
```

- 生产者：`OutboxPublisher` 后台服务，开 publisher confirms，批量确认后标记 `published_at`；MQ 不可用时静默重试，消息留在 outbox 不丢。
- 消费者：`prefetchCount` 限流、手动 ack、单条失败不影响批次；幂等三件套 = Redis SETNX → MySQL `UNIQUE(event_id)` → 聚合幂等更新。
- 顺序性：同一 `event_id` 的重复消息允许乱序到达，聚合用累加 + 唯一键保证结果收敛，不强求全局有序（写进 README，作为一个主动的取舍说明）。
- 故障注入开关：实验台可一键停消费者 / 停 publisher / 注入重复消息 / 注入处理异常，用于 A4、A5 与压测报告。

## 7. 实时通道与前端

SignalR Hub `/hubs/metrics`，服务端方法：

| 方法 | 载荷 | 频率 |
|---|---|---|
| `MetricsUpdated` | QPS、各类型计数、P99、总事件数 | 每秒一次聚合快照 |
| `EventAppended` | 最近事件（截断 200 条） | 有新增时批量推 |
| `HealthUpdated` | 队列深度、消费延迟、Redis/MySQL 连通性 | 每 2 秒 |
| `AlertFired` | 告警记录 | 触发时 |

服务端**每秒推快照而非逐事件推**，避免高 RPS 下打爆浏览器；断线重连后客户端先拉一次 REST 快照再续接增量。前后端 DTO 用同一份 TypeScript 类型定义，纳入 Vitest 契约测试。

页面清单：

| 路由 | 内容 |
|---|---|
| `/` 实时大屏 | QPS 折线（60s 滑窗）、事件类型分布、最近事件流、系统健康卡、告警时间线 |
| `/lab` 实验台 | 流量生成器（RPS / 类型 / 时长 / 注入重复与错误）、三个一键实验及结果表、消费者开关 |
| `/rules` 规则页 | 告警规则 CRUD、通知渠道切换（Mock / Webhook）、发送记录 |
| `/reports` 报告页 | 压测与故障注入数据，可重跑并导出 Markdown |

## 8. 旁路模块（企业微信）

边界写死：**只订阅结果，不参与主链路**。企业微信不可用时只写 `notification_log` 失败记录并触发大屏提示，绝不阻塞事件处理。

两类消息：
1. **异常告警**：规则引擎评估 Redis 健康快照，超阈值触发；`alert:silence:{rule_id}` 静默窗口去重；死信队列有新消息时也会触发。
2. **每日定时日报**：Quartz cron `0 0 9 * * ?`（Asia/Shanghai），从 `event_daily_agg` 查昨日数据，格式化后发送；`biz_key = daily:{yyyy-MM-dd}` 保证同日只发一次。

接入路径分两期：一期 `WebhookChannel`（群机器人，无需资质与 access_token，半小时跑通）；二期 `WecomAppChannel`（自建应用，按 userid 发模板卡片、收回调，需管理员权限与可信 IP）。默认 `MockChannel`，保证零外部依赖也能演示完整链路。

## 9. 测试策略

按 Superpowers 的红绿 TDD，以下必须**先有失败的测试**再写实现：

| 层 | 测试 |
|---|---|
| 单元 | 滑窗计数与窗口边界、幂等判定、告警阈值与静默、日报格式化、DTO 契约 |
| 集成（Testcontainers） | `events` 与 `outbox` 同事务、MQ 停机后 outbox 不丢、重复投递 1000 条计数不变、失败三次进 DLQ、Redis 清空后可从 MySQL 重建、日报同日只发一次 |
| 前端（Vitest） | 指标窗口裁剪、SignalR 消息合并 reducer、断线重连补快照 |
| 压测 | 内置 generator（可配 RPS）或 k6；记录吞吐、端到端 P99、丢失率、积压追平耗时 |
| 故障注入 | 停消费者 / 停 MQ / 杀 publisher 三个场景各跑一遍，结果写进 `/reports` |

## 10. 里程碑与完成判据

| 里程碑 | 内容 | 完成判据 |
|---|---|---|
| M0 | `git init` + 解决方案骨架 + docker compose + 健康检查 + CI，测试基线全绿（或全空） | `docker compose up -d` 全绿，`dotnet test` 通过 |
| M1 | 事件接入 API + outbox + publisher confirms | 集成测试：MQ 停机时 outbox 保留、恢复后补投 |
| M2 | 消费聚合 + 幂等 + Redis 窗口 + MySQL 明细/日聚合 | 重复投递 1000 条计数不变；对账一致 |
| M3 | SignalR Hub + Vue 大屏 + ECharts + 流量生成器 | A2 达成，大屏可见地跑 |
| M4 | 可靠性闭环：重试 / 死信 / 积压监控 / 故障注入开关 | A4、A5 达成，三个实验有数据 |
| M5 | 旁路模块：告警规则 + Quartz 日报 + Mock/Webhook 渠道 | A7 达成，企微断开不影响主链路 |
| M6 | 作品集收尾：压测报告、15 秒 GIF、README 架构图、实验数据页 | A1、A6、A8 全部达成 |

每个里程碑结束后做一次代码评审（先查规格符合度，再查代码质量），Critical 级问题阻断推进。

## 11. 风险与未决

- **方向未显式拍板**：本文档基于默认假设，确认前不进入实施。
- **工作区无 git 仓库**：M0 需先 `git init` 并做一次初始提交，才能按 Superpowers 开隔离 worktree。
- **本机工具链**：需 .NET 10 SDK 与 Node 22+/24 LTS；M0 先跑版本复核，缺什么在 README 写明安装方式。
- **企业微信权限未知**：默认 Mock，不影响交付。
- **在线 demo 待定**：作品集加分项，但需要云资源与域名，等本地版完成后再决定。
- **不堆技术**：明确拒绝 Kafka / ES / 微服务 / 多租户，除非用户后续明确要求。

## 12. 下一步（Superpowers 流程）

1. 用户确认或修订本文档 → 规格锁定。
2. `git init` + 初始提交 → 开隔离工作区（分支 / worktree）→ 确认干净的测试基线。
3. 写实施计划：把 M0–M6 拆成 2–5 分钟粒度的任务，每个含精确文件路径、完整代码、验证步骤。
4. 子代理驱动开发，每个任务两段式评审；实现严格 RED-GREEN-REFACTOR。
5. 里程碑间代码评审，收尾时跑全量测试并给出合并 / 保留 / 丢弃选项。

**当前阶段不写任何业务代码。**
