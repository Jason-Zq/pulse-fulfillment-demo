# 订单履约中台 — 开发规格说明书（SPEC）v0.1

- 状态：**已确认**（2026-11-06 用户确认后落盘，作为本项目正式规格）
- 路径：`docs/SPEC.md`
- 日期：2026-11-06
- 流程位置：Superpowers 第 1 步「头脑风暴」产出，已签字；现进入第 2 步（git init + 隔离工作区）与第 3 步（实施计划）。

## 1. 目标与定位

一个**可公网访问的作品集演示站**，用一条完整的订单履约链路把 MySQL / Redis / RabbitMQ 讲透，并附一份**写清楚的边界说明**，明确它不是生产系统。

| 编号 | 目标 |
|---|---|
| G1 | 面试官打开链接 30 秒内看懂系统在做什么，`docker compose up` 可本地复现 |
| G2 | 三个中间件各对应一个能讲五分钟的真实问题，且都有实测数据支撑 |
| G3 | 一份边界说明，讲清「为什么这不是生产系统、要上线还差什么」 |

## 2. 范围

**做**：商品与库存、下单与模拟支付、订单状态流转、异步履约、超时关单、死信人工补偿、对账报表、实验台、演示数据与流量生成器、演示站部署、边界说明文档。

**不做**：真实支付与外部 IM、营销 / 优惠券 / 秒杀活动、多商户、物流对接、App / 小程序、完整权限体系（仅一个演示账号）、真实用户个人信息、高可用与灾备、生产级审计与合规。

## 3. 技术栈（锁定）

| 层 | 选型 |
|---|---|
| 后端 | **.NET 8** / ASP.NET Core / C# |
| 存储 | **MySQL**（事实源，EF Core + Pomelo） |
| 缓存 | **Redis**（StackExchange.Redis） |
| 消息 | **RabbitMQ**（RabbitMQ.Client） |
| 前端 | **Vue 3 + Element Plus + Vite** |
| 测试 | xUnit + Testcontainers.NET；Vitest |
| 编排 | Docker Compose |

零外部服务依赖。已知风险：**.NET 8 官方支持于 2026-11-10 结束**，本地 / CI / 演示站不受影响，该项写入边界说明，不改动用户决定。

## 4. 功能规格

### 4.1 用户端（极简，只为给链路一个入口）

商品列表 → 购物车 → 下单（带幂等 token）→ 模拟支付结果页。不设营销页、不设用户注册。

### 4.2 运营后台（Element Plus）

| 路由 | 页面 | 关键元素 | 对应技术点 |
|---|---|---|---|
| `/admin/dashboard` | 仪表盘 | 今日订单、GMV、库存告警、队列深度卡片 | 聚合查询 |
| `/admin/products` | 商品与库存 | `el-table` 库存三态、批量改价、`el-dialog` 表单 | MySQL 乐观锁 |
| `/admin/orders` | 订单列表 | 多条件筛选、状态标签、分页、批量发货 | 状态机 / 索引 |
| `/admin/fulfillment` | 发货与售后 | 发货单、售后简化流转 | 异步履约 |
| `/admin/reports` | 对账报表 | 四方对账（订单 / 明细 / 库存流水 / 日报） | 聚合一致性 |
| `/admin/dead-letters` | 死信补偿台 | 失败消息列表、错误详情、一键重试 | DLQ / 人工补偿 |
| `/admin/alerts` | 系统告警 | 站内告警记录、静默状态 | Redis 去重 |
| `/admin/lab` | 实验台 | 流量生成器、故障注入开关、三个实验与结果表 | 全部中间件 |

### 4.3 实验台能力

一键造流量（可配 RPS / 时长）、注入重复消息、注入履约异常、停 / 启消费者、停 / 启 OutboxPublisher、一键清空演示数据。

## 5. 领域模型与状态机

订单状态机：

```
Created(待支付) ──支付──> Paid(待发货) ──发货──> Shipped ──确认──> Completed
     │                        │
     └──超时/取消──> Cancelled  └──售后──> Refunding ──> Refunded
```

库存三态：`available`（可售）→ `reserved`（下单预扣）→ `deducted`（支付后实扣）；超时关单释放 `reserved` 回 `available`。预扣与实扣写 `inventory_ledger` 流水，作为对账依据。

| 表 | 关键字段 | 约束 |
|---|---|---|
| `products` | `id`, `sku`, `name`, `price`, `status` | `UNIQUE(sku)` |
| `inventory` | `sku` PK, `available`, `reserved`, `version` | `version` 乐观锁 |
| `orders` | `id`, `order_no`, `status`, `total_amount`, `created_at`, `paid_at`, `expires_at`, `version` | `UNIQUE(order_no)`，`INDEX(status, created_at)` |
| `order_items` | `id`, `order_id`, `sku`, `qty`, `price` | `INDEX(order_id)` |
| `inventory_ledger` | `id`, `sku`, `change_type`, `qty`, `ref_order_id`, `created_at` | 只追加，对账用 |
| `outbox` | `id`, `event_id`, `routing_key`, `payload`, `status`, `retry_count`, `created_at`, `published_at` | `UNIQUE(event_id)`，`INDEX(status, created_at)` |
| `processed_events` | `event_id` PK, `processed_at` | 消费端幂等最终保障 |
| `dead_letters` | `id`, `original_event_id`, `payload`, `error`, `retry_count`, `status`, `created_at`, `handled_at` | 人工补偿 |
| `daily_reports` | `stat_date` PK, `order_cnt`, `paid_cnt`, `cancel_cnt`, `gmv`, `created_at` | 日报幂等 |
| `experiment_results` | `id`, `name`, `params`, `result`, `ran_at` | 实验数据留痕 |
| `qrtz_*` | Quartz 官方表 | 官方脚本生成 |

Redis key：库存预扣 Lua 原子操作、`idem:{token}` 幂等 token、`dedup:{event_id}` 消费短钥匙（TTL 24h）、`alert:silence:{key}` 告警静默、健康快照。

## 6. 消息与可靠性设计

```
POST /api/orders ──同一 MySQL 事务──> orders + order_items + outbox
                                        │ OutboxPublisher (publisher confirms)
                                        v
                              ex.orders (topic, durable)
                    ┌───────────────────┼────────────────────┐
              q.fulfillment        q.order.timeout        q.fulfillment.retry
              (order.paid)        (TTL+DLX 15min)        (TTL 5s, 3 次)
                    │                   │                       │
              异步履约/实扣      超时未支付关单+释放           q.dlq.fulfillment
                    └────失败───────────┴────超过重试次数──────────┘
```

关键机制：

1. **事务性 outbox**：`orders` 与 `outbox` 同事务，后台 Publisher 批量投递、开 publisher confirms、成功后标记 `published_at`。解决「DB 写成功但 MQ 没发出去」。
2. **端到端幂等**：生产端 `event_id` 唯一；消费端 Redis `SETNX` 短钥匙 + `processed_events` 唯一键双保险；库存扣减走乐观锁与流水，重复消息不重复扣。
3. **消费语义**：手动 ack、`prefetchCount` 限流、单条失败不影响批次；至少一次投递 + 幂等消费 = 不丢不重。不保证全局顺序，聚合结果收敛即可。
4. **超时关单**：用 **TTL + DLX**，不引入 `rabbitmq_delayed_message_exchange` 插件——演示环境超时时长固定（15 分钟，可配），队头阻塞问题不存在，且 compose 少一个插件依赖。此取舍写入 README。
5. **失败补偿**：重试 3 次后入 `q.dlq.fulfillment`，落 `dead_letters` 表，后台补偿台可人工重试。

## 7. 接口清单

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/api/products` | 商品与可售库存 |
| POST | `/api/orders` | 下单，带 `Idempotency-Key` |
| POST | `/api/orders/{id}/pay` | 模拟支付 |
| POST | `/api/orders/{id}/cancel` | 取消 |
| POST | `/api/orders/{id}/ship` | 发货 |
| GET | `/api/orders` | 列表（状态 / 时间 / SKU 筛选、分页） |
| GET | `/api/orders/{id}` | 详情含状态时间线 |
| GET | `/api/reports/daily?date=` | 站内日报与四方对账结果 |
| GET | `/api/dead-letters` | 死信列表 |
| POST | `/api/dead-letters/{id}/retry` | 人工补偿 |
| POST | `/api/lab/traffic` | 造流量 |
| POST | `/api/lab/inject` | 故障注入（重复 / 异常 / 停消费者 / 停 Publisher） |
| POST | `/api/lab/reset` | 清空演示数据并重新播种 |
| GET | `/api/experiments` | 实验结果 |
| GET | `/api/health` | 各依赖健康状态 |

## 8. 前端规格

- 路由：用户端 `/`、`/cart`、`/checkout`、`/order/:id/result`；后台 `/admin/*`（上表 8 页）。
- Element Plus 用满：`el-table` + 自定义列与状态标签、`el-form` 复杂筛选、`el-dialog` 表单、`el-pagination`、批量操作、`el-descriptions` 详情时间线。
- **实时性：不用 SignalR**。后台订单页 3 秒轮询拉取列表，任何写操作（支付 / 发货 / 补偿重试）后立即重新拉取。理由：管理后台是低频查看页面，RabbitMQ 已承担服务端异步链路的实时性，为「状态自动跳」维护浏览器长连接属于过度设计；浏览器也不直连 RabbitMQ（Web-STOMP / Web-MQTT 会把内部消息总线暴露到公网、绕过业务层鉴权与聚合）。
- TypeScript 类型与后端 DTO 对齐，纳入 Vitest 契约测试；前端单测覆盖筛选条件、状态标签映射、指标窗口裁剪。

## 9. 定时任务（全站内）

- **每日对账报表**：Quartz.NET（ADO.NET JobStore 持久化到 MySQL），cron `0 0 1 * * ?`（Asia/Shanghai），从订单与流水聚合昨日数据写 `daily_reports`，`stat_date` 唯一保证重跑幂等。
- **超时关单**：走 MQ 延迟消息，不用定时任务（规则可配、逐单到期）。
- 所有时间 UTC `DATETIME(3)` 存储，前端按 Asia/Shanghai 展示。

## 10. 验收标准

| 编号 | 判据 |
|---|---|
| A1 | `docker compose up -d` 后 2 分钟内站点可访问，`/api/health` 全绿 |
| A2 | README 首页 30 秒 GIF 的完整链路可手动复现 |
| A3 | 四方对账一致：订单 / 明细 / 库存流水 / 日报，差异为 0 |
| A4 | 停掉 RabbitMQ 后下单不报错，恢复后 outbox 补投成功，订单不丢 |
| A5 | 重复投递 1000 条相同消息，库存不重复扣、流水不重复写 |
| A6 | 并发下单压测下超卖率为 0，报告给出吞吐与 P99 |
| A7 | 超时未支付的订单准确关单且预扣库存释放 |
| A8 | 死信消息可在补偿台人工重试并成功 |
| A9 | 演示站公网可访问，边界说明页可查，演示数据可一键清空 |
| A10 | `dotnet test` 与 `vitest run` 全绿，核心链路 RED 测试先于实现 |

## 11. 三个可复现实验

| 实验 | 操作 | 判据 | 对应中间件 |
|---|---|---|---|
| E1 消息不丢 | 停 MQ → 持续下单 → 恢复 MQ | outbox 全部补投，订单与流水一致 | MySQL + RabbitMQ |
| E2 消费幂等 | 注入 1000 条重复 `event_id` | 库存扣减次数 = 1，流水条数 = 1 | Redis + MySQL |
| E3 并发不超卖 | 流量生成器并发抢同一 SKU | 超卖率 0，记录吞吐 / P99 / 恢复耗时 | Redis + MySQL |

实验结果落 `experiment_results`，在 `/admin/lab` 与 README 同时展示。

## 12. 演示站与边界说明（点名交付物）

### 12.1 部署规格

`docker compose` 起 `mysql` / `redis` / `rabbitmq` / `api` / `web` 五个服务；`api` 暴露健康检查；前端反向代理到 `api`；首页与页脚固定标注「演示环境 · 全部数据为模拟 · 不涉及真实交易」；提供 `/api/lab/reset` 一键清空数据；不收集任何真实个人信息，不设注册。

### 12.2 边界说明文档（独立文档 `docs/BOUNDARIES.md`）

大纲：① 本项目是什么 / 不是什么；② 演示数据声明与可清空说明；③ 明确未实现的能力清单（真实支付、完整认证授权、高可用与备份、审计留痕、合规）；④ 要上线生产还差什么（支付资质需企业营业执照与备案域名、ICP 备案、电子商务法的经营者登记与交易记录保存、个人信息保护、单实例无灾备、金额篡改与回调伪造防护、.NET 8 已 EOL 需升级）；⑤ 上线三档结论（演示站可做 / 内测记单需补齐 / 真实经营不做）；⑥ 技术取舍记录（TTL+DLX、outbox、RabbitMQ 而非 Kafka、3 秒轮询而非 SignalR）。

### 12.3 上线三档结论

公网作品集演示站 = **目标**；内测记单不收钱 = 需先补认证授权、备份、监控、日志；对外经营真实收款发货 = **不做**，卡点在资质、合规与运维责任，不在技术。

## 13. 里程碑

| 里程碑 | 内容 | 完成判据 |
|---|---|---|
| M0 | `git init` + 解决方案骨架 + compose + 健康检查 + CI，测试基线全绿/全空 | A1 |
| M1 | 领域模型、商品库存、下单与库存预扣（TDD：乐观锁、幂等 token） | 单测全绿 |
| M2 | outbox + publisher confirms + RabbitMQ 拓扑 | A4 |
| M3 | 履约消费 + 幂等 + 超时关单 + 重试死信 + 补偿台 | A5 / A7 / A8 |
| M4 | Element Plus 用户端与后台核心页（订单 / 库存 / 发货 / 对账） | A2 可手动跑通 |
| M5 | 实验台 + E1/E2/E3 + 压测报告（后台状态用 3 秒轮询，不引入 SignalR） | A3 / A6 |
| M6 | 边界说明文档 + README 架构图 + 演示 GIF + 一键重置 | A9 / A10 |
| M7 | 演示站公网部署（可选，核心完成后再做） | 公网可访问 |

里程碑间做两段式代码评审（先规格符合度，再代码质量），Critical 级问题阻断推进。

## 14. 测试策略

严格 RED-GREEN-REFACTOR，测试先于实现。单元：库存预扣与乐观锁、订单状态机非法跳转、幂等判定、对账计算、日报幂等。集成（Testcontainers 真容器）：outbox 同事务、停 MQ 补投、重复消息不重复扣、超时关单释放库存、三次失败进 DLQ、四方对账一致。前端：筛选条件、状态标签映射、DTO 契约。压测：流量生成器 + 实测数据表。

## 15. 风险与未决

- .NET 8 已 EOL：不影响演示，写入边界说明。
- 演示站部署资源（域名 / 主机）待定，M7 再决定，不阻塞 M0–M6。
- RabbitMQ 延迟方案定为 TTL+DLX，若后续需要多种超时时长再评估插件。
- 实时刷新方案已定为 3 秒轮询，不引入 SignalR；若后续新增高频实时监控大屏再评估 SSE / SignalR。
- 工作区目前为空且不是 git 仓库，M0 需先 `git init`。

## 16. 已确认的决策

1. 方向 = **A 电商订单履约中台**。
2. 深度主轴 = **消息可靠性闭环**（outbox → 幂等 → 死信补偿），高并发一致性作为实验数据呈现，不单独做秒杀场景。
3. **保留极简用户端下单页**（商品 → 购物车 → 下单 → 模拟支付）。
4. 上线目标 = **可公网访问的演示站 + 边界说明**，M7 可选。
5. **删除 SignalR**，后台状态刷新改为 3 秒轮询 + 写操作后立即刷新（见第 8、13、15 节）。
6. 企业微信已移除，项目零外部依赖。

## 17. 下一步

`git init` + 初始提交 → 开隔离工作区并确认干净测试基线 → 写实施计划（M0–M6 拆成 2–5 分钟粒度任务，含精确路径、完整代码、验证步骤）→ 红绿 TDD → 里程碑评审 → 收尾。

**规格已锁定。实现阶段任何变更须回到本文更新，不允许悄悄改。**
