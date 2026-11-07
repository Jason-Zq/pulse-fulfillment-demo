# 边界说明 · Pulse 订单履约中台

> 本文是交付物之一，不是免责声明模板。目标是让看项目的人清楚知道「这是什么、不是什么、要上线还差什么」。

## 1. 本项目是什么 / 不是什么

**是**：一个可公网访问的作品集演示站，用一条完整的订单履约链路展示 MySQL / Redis / RabbitMQ 各自的真实用途，并给出可复现的实验与验证数据。

**不是**：一个可以对外经营、收钱、发货的电商系统。没有真实商户、没有真实商品、没有真实用户、没有资金流。

## 2. 演示数据声明

- 全部 SKU、订单、库存流水、报表数据均为程序生成的模拟数据。
- 不收集姓名、手机号、收货地址、支付账号等任何真实个人信息；不提供注册。
- 演示数据可一键清空并重新播种：**`POST /api/lab/reset` 已实现并实测**（清空订单 / 明细 / 库存流水 / outbox / processed_events / 死信，重新播种 2 个 SKU，保留日报 / 实验结果 / 告警 / 控制表留痕）；也可整卷重置 `docker compose down -v && docker compose up -d`。
- 页面顶部固定标注「演示环境 · 全部数据为模拟 · 不涉及真实交易」。

## 3. 明确未实现的能力

| 类别 | 未实现 |
|---|---|
| 交易 | 真实支付、退款资金流、发票、对账结算 |
| 业务 | 营销 / 优惠券 / 秒杀活动、多商户、物流对接、App / 小程序 |
| 账号 | 完整认证授权体系（仅一个演示账号）、多租户、权限审计 |
| 可靠性 | 高可用、主从 / 集群、备份恢复、灾备、限流熔断 |
| 运维 | 监控告警平台、日志集中留存、审计留痕、灰度发布 |
| 调度 | **Quartz ADO.NET JobStore 与 `qrtz_*` 官方持久化表**（SPEC 第 5/9 节）：M5 用 `RAMJobStore`，重启后重新调度；靠 `stat_date` 唯一键 upsert 保证日报不重复，但触发器 / 错过触发（misfire）不持久化 |
| 并发 | **乐观锁冲突的自动重试 / 队列化**：`Inventory.Version` 冲突直接返回 409「请重试」，E3 实测 100 并发仅 11 单成功。保证超卖率 0，但高并发成功率低；生产系统需重试退避或串行化 |
| 合规 | ICP 备案、等保、隐私政策、消费者权益与售后流程 |
| 链路可见性（M6c 6.13 决议：**明确不做**） | `GET /api/orders/{id}` 的 `evidence?: { outboxEventId, outboxStatus, processedEvent, ledgerCount, deadLetterCount }` 子对象，以及依赖它的单笔 `/admin/pipeline` 追踪页：SPEC v0.2 允许延后到 M6c，M6c 决议以「Dashboard 7 节点泳道 + 可点击下钻」满足 G2'，不做单笔链路追踪。订单详情页在字段缺失时**明确显示「不伪造 0」**，只给中间件 / 真实键表名证据卡；全局真实计数看 `GET /api/pipeline` 与 `/api/dashboard` |
| 运维呈现（M6d / M7 已完成） | M6d 已完成：`docs/architecture.svg` 全链路架构图 + `tests/e2e/a11-click.mjs` 真实 chromium 浏览器点击实测（**21/21 PASS**）+ 演示媒体（webm 录屏已生成；GIF 需完整 ffmpeg，环境仅有 Playwright 精简版 ffmpeg，缺 GIF muxer）。M7 已完成：生产加固（`docker-compose.prod.yml` 收口宿主端口 + Redis/RabbitMQ 口令 + `Swagger__Enabled=false`）、Caddy 边缘 TLS（`deploy/Caddyfile` + `deploy/docker-compose.edge.yml`）、部署指南（`docs/DEPLOYMENT.md`）；`Swagger__Enabled=false` 关闭路径已用生产覆盖干净重建真验（nginx 与 api 直连 `/swagger/...` 均 404）；真实公网域名 TLS 部署需自备域名/服务器/ICP，仅作文档、未在本机跑真实证书签发 |

## 4. 要上线生产还差什么

1. **支付资质**：微信支付 / 支付宝商户号需要企业营业执照与已备案域名，个人主体拿不到。
2. **合规**：ICP 备案；电子商务法下的经营者登记、商品信息披露、交易记录保存不少于三年；个人信息保护法下的手机号与收货地址处理；发票与售后责任。
3. **架构**：当前是单实例 MySQL / Redis / RabbitMQ，没有高可用、备份恢复与灾备；真实订单丢失即事故。
4. **安全**：金额篡改、支付回调伪造、重放攻击、越权访问、敏感数据加密、密钥管理，均不在演示范围。
5. **框架**：本项目按用户指定使用 **.NET 8**，其官方支持已于 **2026-11-10 结束**。本地运行与演示站不受影响，但生产使用属于安全与合规问题，正式上线前需升级到受支持的 LTS（如 .NET 10）。
6. **运维责任**：真实交易意味着客服、对账、差错赔付与值班响应，这不是代码问题。

## 5. 上线三档结论

| 上线含义 | 结论 |
|---|---|
| **作品集演示站**（公网可访问、全模拟数据） | **本项目目标**。标注演示声明、数据可清空 |
| 内测 / 熟人小店记单（不收钱、不存真实个人信息） | 可行，但需先补齐认证授权、备份、监控、日志留存 |
| 对外经营、真实收款发货 | **不做**。卡点在资质、合规与运维责任，不在技术能力 |

一句话：**拦住它的不是 Redis / RabbitMQ 用得好不好，是资质、合规和运维责任。**

## 6. 技术取舍记录

| 取舍 | 选了 | 没选 | 原因 |
|---|---|---|---|
| 消息可靠性 | 事务性 outbox + publisher confirms | API 直接发 MQ | 避免「DB 写成功但消息没发出去」 |
| 超时关单 | TTL + DLX | `rabbitmq_delayed_message_exchange` 插件 | 演示超时时长固定，compose 少一个插件依赖 |
| 后台实时刷新 | 3 秒轮询 | SignalR / SSE | 管理后台低频查看，长连接属于过度设计；浏览器不直连消息总线 |
| 消息中间件 | RabbitMQ | Kafka | 要的是任务队列的重试 / 死信 / 逐条确认，不是日志重放 |
| 定时报表 | Quartz.NET（站内报表） | 外部 IM 定时推送 | 项目零外部依赖，`docker compose up` 即可完整演示 |
| 定时调度存储（M5 计划偏移） | 真实 `Quartz` 3.13.0 + `RAMJobStore`，cron `0 0 1 * * ?`（Asia/Shanghai），结果按 `stat_date` 唯一键幂等写 `daily_reports` | SPEC 第 5/9 节的 **ADO.NET JobStore + `qrtz_*` 官方表** | 后者需按 provider 引入 `MySqlConnector` / `Microsoft.Data.Sqlite` 与两份官方 DDL，本机无法对 MySQL JobStore 做完整验证；单实例演示站重启后 Quartz 会重新调度，靠唯一键 upsert 保证不产生重复日报。**这是有意且已记录的规格偏移，不写成「已实现 ADO.NET JobStore」** |
| 实验台控制面（M5） | 开关落 `lab_controls` 表（跨 API / Worker 两个进程读写） | Redis 开关标志 | 本地 SQLite 演示模式没有 Redis；DB 开关在 SQLite 与 MySQL 行为一致，Redis 保留给 SPEC 指定的 `alert:silence:{key}` / 告警去重 |
| 仪表盘队列深度（M5） | RabbitMQ.Client `QueueDeclarePassive(queue).MessageCount` | RabbitMQ management HTTP API | 避免 guest 远程登录限制与额外凭据配置；不可用时如实返回 `available:false`，不伪造 0 |
| 多明细事实源（M6b） | 履约 / 超时关单按 `order_items` 逐行扣减 / 释放 | 只按 MQ payload 的首行 `sku/quantity` | payload 仍带首行字段与 `items[]` 仅为兼容 M1/M3 单消息语义；只按首行处理会漏扣 / 漏放其余 SKU，造成库存泄漏。`order.Items.Count == 0` 时回退 payload，保证探针 / 历史消息语义不变 |
| 商城视觉（M6b） | 自写 CSS + 内联 SVG 占位图 + `ShopLayout` 顶部导航 | 新 UI 库 / 商品图 CDN / SSR | 范围约束「不引入新 UI 库、零外部资源」；Element Plus 继续负责后台与表格 / 表单控件。`App.vue` 静态接入 `ShopLayout` 使主 chunk 由 171.27 kB 增至 255.15 kB，仍 < 500 kB 且无构建告警（M6c 为 255.21 kB） |
| 泳道数据源（M6c） | `GET /api/pipeline` 聚合端点，7 节点各自查真实表 / `QueueDeclarePassive`，前端只读该端点 | 前端分别调 7 个接口拼装 / 在页面里写死示意数字 | 「界面本身就是技术叙事」要求数字真实可追问；单点聚合让 8 个后台页与首页共用同一份事实源，队列不可用时 `count=-1 + available=false`，前端显示「—」不伪造 0。验收脚本逐项把 6 个节点计数与 MySQL 真实行数、1 个节点与 RabbitMQ 管理台深度比对 |
| Swagger 暴露（M6c / M7 已验关闭） | 容器 Production 下默认启用，用 `Swagger__Enabled=false` 可关；nginx 加 `/swagger/` 反代 | 只在 `Development` 开放 / 改 `docker-compose.yml` 加环境变量 | 演示站需要「给别人看的接口浏览入口」，但公网部署应显式关闭。M7 已用生产覆盖干净重建注入 `Swagger__Enabled=false` 真验：nginx 8080 与 api 直连的 `/swagger/v1/swagger.json`、`/swagger/index.html` 均返回 **404**（不再是 SPA 回退 200），证明 Swagger 公开面已关闭；默认开放双向实测通过 |

## 7. 验证状态（如实记录，2026-11-07 M6c + M6d 真容器验收收尾）

### 7.1 已实测通过

**A 层（不依赖 Docker）**

- `dotnet build src/Pulse.sln`：**0 warning / 0 error**。
- `dotnet test src/Pulse.sln`：**211 passed**（Domain 3 + Infrastructure 159 + Api 49），M6b 基线 207 → M6c 新增 4（`PipelineService` 服务层 + `/api/pipeline` 端点集成测试）。M0–M6c 每轮 RED→GREEN 均保留编译失败 / 断言失败证据；M1 API 薄适配层「先实现后补测」、M5 `persisted` 字段未单独先红两处纪律偏差已记录在 `docs/PLAN.md`。
- `npm test`：**167 passed**（26 个 Vitest 文件，M6b 149 → M6c 新增 18）；`npm run build` = `vue-tsc --noEmit`（严格模式 0 error）+ Vite 构建 0 error，最大 chunk **255.21 kB**，仍 < 500 kB（增量原因见第 6 节）。
- **本地端到端契约联调**：M4 **33 / 33 PASS**；M5 **30 / 30 PASS**（主会话按 `web/src/api/types.ts` 逐字段核对真 API 响应，含 `id` number、`params`/`result` 为 JSON 字符串、默认开关 true/true/false、静默不存在的 key 返回 404 的契约边界）。另起 `vite dev`(5173) 验证 `/api` 代理。
- **并行裂缝修复（记录在案）**：M4 两边测试全绿但主会话逐字段核对发现 5 处 DTO 不一致，链路实际跑不通；M5 因此**先锁死 DTO 契约表再派并行实现**，本地复核 30/30 无裂缝。印证「测试绿 ≠ 联调通」。

**C 层（Docker Desktop 29.9.2 / macOS arm64，真容器）**

- `docker compose up -d --build` **实测通过**：`mysql` / `redis` / `rabbitmq` 三个中间件 healthcheck 全 healthy，`api` / `worker` / `web` 三个多阶段镜像构建成功（容器内 `dotnet restore`、`dotnet publish`、`npm install`、`npm run build` 均跑通）。`docker compose down -v && docker compose up -d` 干净重启也复跑通过。
- **MySQL 8 迁移真实应用**：`InitialCreate`、`20261106173451_M3ConsumptionAndCompensation`、`20261107072341_M5LabAndReports` **三条**写入 `__EFMigrationsHistory`，**12 张业务表**建成并自动播种 2 个 SKU。此前「迁移只生成未应用」的状态已解除；容器内 `dotnet restore` 成功还原新增的 `Quartz` 3.13.0。
- **Redis 7 真连接**：消费端 `dedup:{event_id}` SETNX 短钥匙真实写入（TTL 24h），不再是空实现。
- **RabbitMQ 3.13 真连接**：`ex.orders`(topic) + 4 个队列声明成功，`q.fulfillment` / `q.dlq.fulfillment` 各 1 个消费者在线；`q.order.timeout` 核对到 `x-message-ttl=900000` + `x-dead-letter-routing-key=order.timeout`；publisher confirms、手动 ack、`prefetchCount=10` 均真实生效。
- **容器端到端脚本（M4 阶段）`python3 scripts/verify_docker_e2e.py --deep`：64 / 64 PASS**。覆盖：nginx 反代 / health、商品 DTO 8 字段、下单幂等与库存预扣、outbox `pending→published`、支付 → `Paid → Shipped` + `Deduct` 流水 + `processed_events`、订单列表 / timeline / 四方对账 `diff=0` / 批量改价 / 超卖 409 / 状态机 404、取消释放预扣、超时关单、三次重试入死信（`RetryCount=3`）、补偿台人工重试生成新 event_id 重投。
- **M5 扩展后 `python3 scripts/verify_docker_e2e.py --deep --m5 --experiments`：干净重启后 132 / 132 PASS**。新增覆盖：9 个端点逐字段契约（含 `queues.available=false` 时显示「—」而非 0）、Redis `alert:silence:{key}` 真实写入与列表静默同步、静默 404 边界、**停 Publisher 期间 outbox 保持 pending、恢复后全部补投（控制开关跨 API / Worker 进程真实生效）**、一键重置保留实验留痕、日报 `persisted` 字段、MySQL 12 张表。
- **M6b 后 `python3 scripts/verify_docker_e2e.py --deep --m5 --experiments --m6`：`docker compose down -v && up -d --build` 干净重建后 153 / 153 PASS**。原 132 条基线**一条未退化**（商品 DTO 断言随规格从 8 字段更新为 9 字段，新增 `version`），M6b 新增 21 条断言。
- **M6b 多明细真链路（真容器）**：`items[]` = SKU-1×2 + SKU-2×3 → 2 行 `order_items`、2 行 `Reserve` 流水（2/3）、两个 SKU 各自 `available` 减、`reserved` 加、`version` 各 +1、1 行 `order.created` outbox；`items` 与 `sku/quantity` 同传 / 空数组均返回 400 `{ error }`；同一幂等键重放返回同一 id；`GET /api/orders?orderNo=` 模糊命中；`GET /api/orders/{id}` 返回 2 行明细；模拟支付返回 `Paid`；Worker **按明细写出 2 行 Deduct（2/3）**、`reserved` 归零、四方对账 `diff=0`（`orderAmount = itemAmount = 796.00`、`ledgerReserveQty=5`）。
- **M6c 后 `python3 scripts/verify_docker_e2e.py --deep --m5 --experiments --m6 --m6c`：`docker compose down -v && up -d --build` 干净重建后 175 / 175 PASS**。M6b 的 **153 条基线一条未退化**，M6c 新增 **22 条**断言；新阶段曾先打旧（M6b）容器留下 22 条全 FAIL 的 RED 证据，证明断言不是空真。
- **M6c 泳道 7 节点真实计数（真容器逐项比对）**：`orders=1 / outbox=0 / exchange=0 / worker=1 / deduct=2 / deadLetter=0 / report=0`；其中 6 项与 MySQL `Orders`、`OutboxMessages(status=pending)`、`ProcessedEvents`、`InventoryLedgers(ChangeType=Deduct)`、`DeadLetters(status=pending)`、`DailyReportRecords` 的真实行数相等，`exchange` 与 RabbitMQ 管理台 `q.fulfillment` 深度相等。DTO 逐字段核对（`PipelineSnapshot` 2 字段 / `PipelineNode` 8 字段）、7 节点 key 顺序、三中间件标签、`available=false` 才允许 `count=-1` 全部通过。
- **M6c Swagger（容器 Production）**：nginx 8080 与 api 5080 直连的 `/swagger/v1/swagger.json` 均 **200 + `application/json`**（不是 SPA 回退 HTML），`openapi=3.0.1`、**22 个 path** 含新增 `/api/pipeline` 与原有 `/api/orders`；`/swagger/index.html` 返回含 `swagger-ui` 的真实 UI 页。另做 SPA 6 条路由与构建产物 JS 的 HTTP 200 冒烟（**不等于浏览器点击验证**），验收后 `/api/lab/reset` 恢复干净演示数据，实验留痕 4 行保留。
- **M7 生产覆盖真容器验收（2026-11-07）**：`docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build` 干净重建 6 服务全起（独立 `pulse_prod` 项目，不与开发 `webproject` 冲突）；`docker port` 确认 mysql/redis/rabbitmq/api/worker/web **全部无宿主端口**；`/api/health`、`/api/products` 经 nginx 反代返回 200；`/swagger/v1/swagger.json` 与 `/swagger/index.html` 经 nginx 与 api 直连**均 404**（Swagger 公开面关闭）；带口令链路 `POST /api/orders`（含 `idempotencyKey`）→ `POST /api/orders/{id}/pay` → 轮询至 `Shipped`，`GET /api/pipeline` 实测 `orders=1 / worker=1 / deduct=1 / deadLetter=0`，证明 Redis `requirepass` / RabbitMQ 独立账号 / MySQL 口令正确接线。验证后 `down -v` 清理，开发演示站（开发基线）未受影响。
- **M6c 后 E1/E2/E3 复跑仍 `passed=true`**：E1 `published=20 / pendingAfter=0 / diff=0`；E2 `confirmed=1000 / deductCount=1 / processedRows=1`；E3 `created=11 / failed=89 / oversoldCount=0`，吞吐 **83.45 RPS**、P99 **40.42 ms**（三次真跑 75.97 / 92.79 / 83.45 RPS、32.08 / 22.18 / 40.42 ms 不同，属正常负载波动，页面只展示 `experiment_results` 最新真实值）。
- **E1 消息不丢**（`passed=true`）：停 Publisher → 20 单 → `pendingBefore=20` → 恢复 → `pendingAfter=0`、`published=20`、对账 `diff=0`。
- **E2 消费幂等**（`passed=true`）：同一 `event_id` 直投 **1000 条** `order.paid`，`confirmed=1000`，该订单 `Deduct` 流水 **1 条**、`processed_events` **1 行**。
- **E3 并发不超卖**（`passed=true`）：100 单 / 并发度 10，从满库存独立起跑 → `created=11`、`failed=89`、`oversoldCount=0`、吞吐 **75.97 RPS**、P99 **32.08 ms**，`reserved=11` 与 `Reserve` 流水 11 一致。独立 100 并发 HTTP 探测取证：**88 个 409 全部为「库存并发冲突，请重试」（乐观锁 `Inventory.Version`），0 个库存不足** —— 高失败率是有意取舍（冲突不自动重试），换来超卖率 0。
- **C 层联调发现并修复 2 个真实缺陷（严格 RED-GREEN，新增 3 个回归测试）**：
  1. **Redis 键双前缀**：`EventDeduplicator.KeyFor()` 产出 `dedup:{id}` 后，`RedisIdempotencyGuard` 又加 `idem:`，实际键是 `idem:dedup:{id}`，与 SPEC 第 5 节不符。修复为 `IdempotencyKeys` 统一管理两个命名空间，已带 `dedup:` 的完整键不再二次加前缀。
  2. **重试 / DLQ 闭环失效（更严重）**：业务失败时 `dedup:{event_id}` 已被 SETNX 占用且从不释放，TTL+DLX 回投的重试消息被判重后直接 ack，`DeadLetters` 永远为空。真 broker 实测证据：探针消息进 `q.fulfillment.retry` 一次后消失，60 秒内 `DeadLetters=0`。修复为可重试失败时调用 `ReleaseAsync` 释放短钥匙（进入 DLQ 的终态保留键），`IIdempotencyGuard` / `IEventDeduplicator` 相应扩展。修复后实测「1 条探针 → 1 条 `RetryCount=3` 死信」。

### 7.2 已知但非缺陷的说明

- `/api/health` 的 `dependencies: { redis: "not_configured", rabbitmq: "not_configured" }` 是 **API 进程自身的静态占位**：M1–M4 期间 API 只连 MySQL，Redis / RabbitMQ 由 Worker 进程托管，因此该字段不表示整个编排未接线。真连接请用 RabbitMQ 管理台 / Redis CLI 或 `scripts/verify_docker_e2e.py` 核对。未擅自改这个响应形状，避免规格外变更。
- **自 M5 起上一条描述不再完全成立**：仪表盘队列深度、重复消息注入、E2 实验需要 API 侧新增只读的 `IQueueDepthProvider` 与 `IMessagePublisher`（懒连接、未配置时降级）。`/api/health` 仍保留静态占位不做形状变更，真实依赖状态改由 `GET /api/dashboard` 的 `source` 与 `GET /api/lab/state` 如实反映。此项列为已知的文档/实现不一致，留待后续里程碑统一决策。
- `Orders.Status` 在 MySQL 中以 `HasConversion<int>()` 存储（如 `2=Shipped`、`4=Cancelled`），语义值以 API 响应为准；联调脚本已按 API 语义断言。
- 人工补偿重试会用新 `event_id` 写一条 pending outbox 重投；若根因未修复（如探针指向不存在的订单），会再次三次失败并生成新死信，这是**预期行为**而非重复缺陷。
- `POST /api/alerts/{key}/silence` 要求 `alertKey` 已存在于 `GET /api/alerts`；不存在的键返回 404 `{ "error": "告警不存在" }`，不做预防性静默。前端只对表格行调用，链路不受影响。该边界在 M5 本地逐字段核对时发现并写入 `docs/PLAN.md` 契约表。
- `GET /api/dashboard` 在 RabbitMQ 不可用时 `queues.available=false` 且四个深度为 **-1**（哨兵值），前端显示「—」。这是「不伪造 0」的约定，不是负数积压。
- 实验台「停消费者 / 停 Publisher / 故障注入」开关落 `lab_controls` 表而非 Redis，以便本地 SQLite 演示模式（无 Redis）也能跨 API / Worker 进程生效；compose 环境实测已确认跨进程生效。
- **M6c 起 Swagger 在 Production 也开放**：这是为演示站「可被追问」有意做的取舍，本地 / CI / 演示不受影响；**公网部署前必须设 `Swagger__Enabled=false`**，否则会把全部 22 个端点（含实验台、故障注入、一键重置）暴露出去。该关闭分支已在 M7 用生产覆盖干净重建真验（nginx 与 api 直连 `/swagger/...` 均 404），见 7.1 / 7.3。
- `GET /api/pipeline` 的 `exchange` 节点在 RabbitMQ 不可用时返回 `count=-1 + available=false`（哨兵值），与 `/api/dashboard` 的 `queues` 同一约定；验收脚本只在依赖在线时断言 `available=true` 且计数与管理台深度相等。

### 7.3 未验证（一律不写「通过」）

- **浏览器实际点击交互（A11 已完成，2026-11-07 真容器实测）**：`tests/e2e/a11-click.mjs` 用真实 chromium 指向 `http://localhost:8080` 跑通 **21/21 PASS**——覆盖商城首屏（三中间件 + 常驻演示声明）→ 商品详情 → 加入购物车 → 购物车 → 结算 → 下单 → **模拟支付 → 轮询到「已发货」全链路闭环**（证明 RabbitMQ → Worker → MySQL 真实跑通）→ 后台 7 节点泳道 7/7（真实计数 `orders=1 / deduct=1 / worker(processed)=1`）+ E1/E2/E3 实测卡 → 8 个后台页统一「本页技术证据」卡 8/8。证据截图见 `docs/screenshots/`。**仍未验证**：真实等待 15 分钟 TTL 自然到期、Quartz 到点自动触发、真实公网域名 TLS 部署（需自备环境）。
- **真实等待 15 分钟 TTL 到期**：已核对队列 TTL / DLX 参数，并通过管理台直投 `order.timeout` 验证了 DLX **之后**的消费路径（订单 `Created → Cancelled`、写 `Release` 流水）；未实际等满 15 分钟观察 `q.order.timeout` 自然到期。
- **Quartz 到点自动触发**：已验证 `StdSchedulerFactory` + cron `0 0 1 * * ?`（Asia/Shanghai）、自定义 `IJobFactory` 解析、Worker 干净启动 0 异常，以及 `DailyReportWriter` 按 `stat_date` 幂等 upsert 的单测；**未实际等到 01:00 触发**并观察 `daily_reports` 自动落行。
- **M7 公网部署已完成（生产加固 + 部署指南）**：`docker-compose.prod.yml`（收口宿主端口、Redis/RabbitMQ 加口令、`Swagger__Enabled=false`）、`deploy/Caddyfile` + `deploy/docker-compose.edge.yml`（Caddy 自动 HTTPS）与 `docs/DEPLOYMENT.md` 部署指南均已交付；用生产覆盖干净重建**真验**宿主端口全部收回、`/api/health`·`/api/products` 200、Swagger 关闭（404）、带口令链路「下单→模拟支付→已发货」跑通（pipeline `orders=1/worker=1/deduct=1/deadLetter=0`）。**真实公网域名 TLS 部署需自备域名/服务器/ICP 备案，仅作文档、未在本机跑真实证书签发**；本文第 4、5 节仍是正式对外经营前的补齐条件。
- **`Swagger__Enabled=false` 关闭路径（M7 已真验）**：已在生产覆盖下注入该环境变量干净重建，nginx 与 api 直连 `/swagger/v1/swagger.json`、`/swagger/index.html` 均 **404**，公开接口文档面已关闭；已从「未验证」移出。
- **单笔订单 `evidence` 链路计数 / `/admin/pipeline` 追踪页**：M6c 6.13 明确不做（见第 3 节），`GET /api/orders/{id}` 不返回该子对象，订单详情页不会假装计数为 0；全局链路可见性由 `/api/pipeline` 泳道满足。
- **Quartz ADO.NET JobStore / `qrtz_*` 官方表未实现**（改用 RAMJobStore）、**乐观锁冲突自动重试未实现**：均为有意取舍，已列入第 3 节未实现清单，不写成「已实现」。
- 售后流转在 `Fulfillment.vue` 为前端 `ElSteps` 演示，**未接后端持久化**，页面已标注。

**M5 已解除的旧「未验证」**：1000 条重复 `event_id` 注入、吞吐 / P99 / 超卖率、故障注入与 E1–E3 结果落库现已在真容器实跑并给出数字（见 7.1）；`/admin/dashboard`、`/admin/alerts`、`/admin/lab`、`POST /api/lab/reset`、日报落 `daily_reports` 均已实现并验收。

未跑过的一律不写「通过」。
