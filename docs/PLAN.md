# 实施计划（PLAN）

- 对应规格：`docs/SPEC.md` v1.0（已确认）
- 日期：2026-11-06
- 流程位置：Superpowers 第 3 步。计划变更必须回头改本文，不悄悄改。

## 0. 环境与隔离基线

实测工具链（2026-11-06）：

| 项 | 结果 |
|---|---|
| .NET SDK | **8.0.423** ✓ 与规格一致 |
| Node / npm | v22.22.2 / 10.9.7 ✓ |
| Git | 2.54.0 ✓ |
| Docker | **本机未安装** ✗ |

**隔离策略**：仓库已 `git init -b main`，文档基线提交在 `main`（`6b7af32`），开发分支 `feat/m0-skeleton`。此处用**分支隔离**而非物理 `git worktree`：工作区本身就是交付目录，另建 worktree 会让代码落到用户看不到的路径。这条取舍在此记录，后续里程碑沿用。

**验证边界**：`dotnet build` / `dotnet test` / `npm run build` 可真跑；`docker compose up`（验收 A1）在本机**无法验证**，compose 文件仍按规格写全，并在 README 与 BOUNDARIES 中标注「未在 Docker 环境验证」。不假装跑过。

## M0 任务（2–5 分钟粒度）

### 任务 0.1：生成 .NET 8 解决方案骨架
- 文件路径：`src/Pulse.sln`、`src/Pulse.Domain/`、`src/Pulse.Api/`、`src/Pulse.Worker/`、`tests/Pulse.Domain.Tests/`
- 要做的：
  ```
  dotnet new sln -n Pulse -o src
  dotnet new classlib -n Pulse.Domain -o src/Pulse.Domain -f net8.0
  dotnet new webapi -n Pulse.Api -o src/Pulse.Api -f net8.0 --no-https
  dotnet new worker -n Pulse.Worker -o src/Pulse.Worker -f net8.0
  dotnet new xunit -n Pulse.Domain.Tests -o tests/Pulse.Domain.Tests -f net8.0
  dotnet sln src/Pulse.sln add src/Pulse.Domain src/Pulse.Api src/Pulse.Worker tests/Pulse.Domain.Tests
  dotnet add tests/Pulse.Domain.Tests reference src/Pulse.Domain
  dotnet add src/Pulse.Api reference src/Pulse.Domain
  dotnet add src/Pulse.Worker reference src/Pulse.Domain
  ```
- 验证：`dotnet build src/Pulse.sln` 退出码 0，无 error。

### 任务 0.2：删除脚手架残留，建立领域层目录
- 文件路径：`src/Pulse.Domain/Class1.cs`、`src/Pulse.Worker/Worker.cs`
- 要做的：删除 `Class1.cs`；`Worker.cs` 保留为 M2 的 OutboxPublisher 占位，先改成空 `BackgroundService`（不写业务逻辑）。
- 验证：`dotnet build src/Pulse.sln` 仍全绿。

### 任务 0.3：写 RED 测试——订单状态机非法跳转
- 文件路径：`tests/Pulse.Domain.Tests/OrderStateMachineTests.cs`
- 要做的：写两个测试——`Created` 不能直接跳到 `Shipped` 应抛 `InvalidOperationException`；`Created → Paid → Shipped → Completed` 合法路径应通过。此时 `OrderStateMachine` 尚不存在。
- 验证：`dotnet test tests/Pulse.Domain.Tests` **必须失败**（编译错误或断言失败），截图/日志留作 RED 证据。

### 任务 0.4：写 GREEN 实现——订单状态机
- 文件路径：`src/Pulse.Domain/OrderStatus.cs`、`src/Pulse.Domain/OrderStateMachine.cs`
- 要做的：定义枚举 `Created/Paid/Shipped/Completed/Cancelled/Refunding/Refunded`，实现 `Transition(current, target)`，只允许 SPEC 第 5 节状态图的边，非法跳转抛 `InvalidOperationException`。
- 验证：`dotnet test tests/Pulse.Domain.Tests` 全绿（2 passed）。

### 任务 0.5：API 健康检查端点
- 文件路径：`src/Pulse.Api/Program.cs`
- 要做的：Minimal API 暴露 `GET /api/health`，返回 `{ status, dependencies }`；依赖检查先返回 `not_configured` 占位（M2 接真实 MySQL/Redis/RabbitMQ 探测）。删除 weather forecast 示例。
- 验证：`dotnet run --project src/Pulse.Api` 后 `curl -s localhost:5080/api/health` 返回 200 与 JSON。

### 任务 0.6：健康检查集成测试（不依赖 Docker）
- 文件路径：`tests/Pulse.Api.Tests/HealthEndpointTests.cs`（新建 xunit 项目 + `Microsoft.AspNetCore.Mvc.Testing`）
- 要做的：用 `WebApplicationFactory<Program>` 断言 `/api/health` 返回 200。
- 验证：`dotnet test src/Pulse.sln` 全绿，含此集成测试。

### 任务 0.7：Vue 3 + Element Plus 前端骨架
- 文件路径：`web/package.json`、`web/vite.config.ts`、`web/index.html`、`web/src/main.ts`、`web/src/App.vue`、`web/src/router/index.ts`、`web/src/views/Placeholder.vue`
- 要做的：Vite + Vue 3 + TS；装 `vue-router`、`pinia`、`element-plus`；`main.ts` 挂载 Element Plus；一个占位路由 `/`；`vite.config.ts` 配 `/api` 代理到后端。
- 验证：`cd web && npm install && npm run build` 退出码 0，`dist/` 生成。

### 任务 0.8：容器编排与环境变量样例
- 文件路径：`docker-compose.yml`、`src/Pulse.Api/Dockerfile`、`web/Dockerfile`、`web/nginx.conf`、`.env.example`
- 要做的：5 个服务（mysql / redis / rabbitmq / api / web），健康检查与 depends_on，端口 8080 对外；`.env.example` 只放非敏感默认值；nginx 反代 `/api`。
- 验证：文件存在且 YAML 可解析（`python3 -c "import yaml..."` 或人工核对）；**`docker compose up` 本机不可验证，如实标注**。

### 任务 0.9：README 与边界说明骨架
- 文件路径：`README.md`、`docs/BOUNDARIES.md`
- 要做的：README 写清一键启动、当前完成度、验证结果、未验证项；BOUNDARIES 按 SPEC 第 12.2 节六节大纲建骨架，重点写「演示环境、非真实交易」与「.NET 8 已 EOL」。
- 验证：两份文件存在且包含上述关键声明。

### 任务 0.10：M0 收尾提交
- 文件路径：整个工作区
- 要做的：`git add -A && git commit -m "feat(m0): 解决方案骨架 + 健康检查 + 首个红绿 TDD 循环"`。
- 验证：`git status --short` 干净；`dotnet test src/Pulse.sln` 与 `npm run build` 同时全绿。

## M1–M6 路线图（进入各里程碑前再拆成 2–5 分钟任务）

| 里程碑 | 关键任务 | 完成判据 |
|---|---|---|
| M1 领域与下单 | 12 张表 EF Core 迁移、商品/库存、下单事务 + 库存预扣 Lua、幂等 token；先写测试覆盖乐观锁与非法状态跳转 | 单测全绿，A3 四方对账可跑 |
| M2 outbox + MQ 拓扑 | OutboxPublisher + publisher confirms、`ex.orders` / `q.fulfillment` / `q.order.timeout` / retry / DLQ；Testcontainers 集成测试 | A4 停 MQ 不丢消息 |
| M3 消费与补偿 | 幂等消费（Redis SETNX + `processed_events`）、超时关单释放预扣、三次重试入死信、补偿台 API | A5 / A7 / A8 |
| M4 前端页面 | 用户端 4 页 + Element Plus 后台核心页（订单 / 库存 / 发货 / 对账），3 秒轮询 | A2 演示链路可手动复现 |
| M5 实验台与压测 | 流量生成器、故障注入开关、E1/E2/E3 结果落库、压测报告 | A3 / A6 |
| M6 交付收尾 | BOUNDARIES 补全、README 架构图 + 演示 GIF、一键重置、全量测试 | A9 / A10 |
| M7（可选） | 演示站公网部署 | 公网可访问 |

## 纪律

1. **RED-GREEN-REFACTOR**：测试先于实现；测试前写的代码一律删除。
2. 每个任务完成后立即跑验证命令，不攒到最后。
3. 里程碑结束做两段式评审（先查 SPEC 符合度，再查代码质量），Critical 级阻断推进。
4. **证据优于声称**：本机无法验证的项目（Docker 相关）明确标注，不写「已通过」。
5. 计划与实际不符时，先更新本文件再继续。

## M0 执行记录（2026-11-06）

- 任务 0.1–0.10 已完成；仅 0.8 的 `docker compose up` 因本机无 Docker **未能验证**，compose 与 Dockerfile 文件已写全。
- 隔离：`main` 基线提交 `6b7af32`，开发分支 `feat/m0-skeleton`。
- 实测：`dotnet build` 0 warning / 0 error；`dotnet test` 4 passed（3 domain + 1 api）；`npm run build` 通过（Vite 6.4.5 / Element Plus 2.x / Vue 3.5）。
- RED 证据：实现前 `dotnet test` 报 `CS0103` 找不到 `OrderStateMachine` / `OrderStatus`，退出码 1。
- **计划偏移（已记录，未悄悄改）**：任务 0.7 原定 `npm create vite` 生成脚手架，该命令在本机被中断（SIGTERM），改为手写等价骨架文件；任务 0.5 的 curl 手工验证由 0.6 的 `WebApplicationFactory` 集成测试覆盖，不再重复起服务。
- 已知提示（非错误）：Vite 构建提示主 chunk >500 kB，M4 拆分后台页面时再做 code-split。
- M1 开始前需先把 M1 拆成 2–5 分钟粒度任务，再进入红绿 TDD。

## M1 任务（2–5 分钟粒度，2026-11-06 拆解）

### 环境补充实测

本机**有** `redis-server 8.10.0`、`mysqld 9.3.0`、`sqlite3 3.54.0`，但**没有 Docker**。前两者版本与规格锁定的 Redis 7 / MySQL 8 不一致，且启动本地 mysqld 需要初始化 datadir、会改动用户机器状态，M1 不启用。

**M1 取舍（记录在案）**：

1. 集成测试统一用 **EF Core Sqlite in-memory**（关系型 provider，支持事务 / 唯一索引 / 乐观锁 `WHERE version = x`），零副作用、CI 可复现；生产仍走 MySQL，MySQL 初始迁移照常生成。M2 若接 Testcontainers 再切真 MySQL。
2. 幂等 token 的 **M1 保障落在 `orders.idempotency_key` 唯一索引**（与订单同事务，比 Redis 短钥匙更硬且可测）；Redis `idem:{token}` 短钥匙留到 M2 叠加，不在 M1 提前造。
3. 原路线图写「12 张表迁移」，按 YAGNI 修正为**只建 M1 需要的 6 张**（products / inventory / orders / order_items / inventory_ledger / outbox）；processed_events / dead_letters / daily_reports / experiment_results / qrtz_* 在各自里程碑建。

### 任务 1.1：新建 Infrastructure 项目并加依赖
- 文件路径：`src/Pulse.Infrastructure/`、`src/Pulse.sln`
- 要做的：`dotnet new classlib -n Pulse.Infrastructure -o src/Pulse.Infrastructure -f net8.0`；`dotnet sln add`；`dotnet add reference src/Pulse.Domain`；加包 `Pomelo.EntityFrameworkCore.MySql 8.0.0`、`Microsoft.EntityFrameworkCore.Design 8.0.*`；Api 与 Worker 引用 Infrastructure。
- 验证：`dotnet build src/Pulse.sln` 0 error。

### 任务 1.2：定义 M1 六个实体
- 文件路径：`src/Pulse.Infrastructure/Persistence/Entities.cs`
- 要做的：`Product`、`Inventory`（含 `Version` 并发令牌）、`Order`（含 `IdempotencyKey` / `ExpiresAtUtc` / `Status`）、`OrderItem`、`InventoryLedger`、`OutboxMessage`；时间统一 UTC。
- 验证：`dotnet build` 通过。

### 任务 1.3：PulseDbContext 与模型配置
- 文件路径：`src/Pulse.Infrastructure/Persistence/PulseDbContext.cs`
- 要做的：六个 `DbSet`；`OnModelCreating` 配 `UNIQUE(sku)`、`UNIQUE(order_no)`、`UNIQUE(idempotency_key)`、`UNIQUE(event_id)`、`Version` 为 `IsConcurrencyToken()`、`INDEX(status, created_at)` / `INDEX(status, created_at)` on outbox。
- 验证：`dotnet build` 通过；后续测试能 `EnsureCreated()`。

### 任务 1.4：生成 MySQL 初始迁移
- 文件路径：`src/Pulse.Infrastructure/Migrations/`
- 要做的：`dotnet ef migrations add InitialCreate --project ... --startup-project src/Pulse.Api --output-dir Migrations`（需 `dotnet-ef` 工具或 Design 包 + `dotnet tool install`）。
- 验证：`Migrations/` 下生成 3 个文件，`dotnet build` 通过。**迁移未应用到真实 MySQL**（本机无对应服务），如实标注。

### 任务 1.5：RED — 下单集成测试
- 文件路径：`tests/Pulse.Infrastructure.Tests/`（新建 xunit + `Microsoft.EntityFrameworkCore.Sqlite 8.0.*`）、`PlaceOrderServiceTests.cs`
- 要做的：四个测试——① 正常下单生成订单 / 预扣库存 / ledger / outbox 各 1 条；② 库存不足抛异常且无副作用；③ 同一幂等键重复提交返回同一订单号、库存只扣一次；④ 乐观锁并发争抢最后一件库存只有一个成功。此时 `PlaceOrderService` 不存在。
- 验证：`dotnet test` **必须失败**（编译错误），留 RED 证据。

### 任务 1.6：GREEN — 实现下单事务
- 文件路径：`src/Pulse.Infrastructure/Orders/PlaceOrderService.cs`
- 要做的：单事务内查幂等键 → 校验库存 → 乐观锁 `available -= qty; reserved += qty; version++` → 建 `Order(Created, expires +15min)` 与 `OrderItem` → 写 ledger(Reserve) 与 outbox(`order.created`) → 提交；`DbUpdateConcurrencyException` 转为明确的库存冲突异常。
- 验证：任务 1.5 的四个测试全绿。

### 任务 1.7：API 接入下单端点
- 文件路径：`src/Pulse.Api/Program.cs`、`src/Pulse.Api/appsettings.json`、`docker-compose.yml`
- 要做的：`Database:Provider` 默认 `Sqlite`（本地演示，无 Docker 也能起）；compose 里覆盖为 `MySql` + 连接串；注册 `PulseDbContext` 与 `PlaceOrderService`；暴露 `POST /api/orders`、`GET /api/products`、`GET /api/orders/{id}`；启动时 `EnsureCreated` 并播种少量演示 SKU。
- 验证：`dotnet test` 含健康检查仍全绿；`dotnet run` 后 `curl POST /api/orders` 返回订单号。

### 任务 1.8：全量验证与 M1 提交
- 要做的：跑 `dotnet build` / `dotnet test` / `npm run build`；更新 README 验证表、BOUNDARIES 第 7 节、PLAN 执行记录；`git commit`。
- 验证：工作区干净，三条命令全绿；**MySQL 迁移应用与 Redis 幂等仍标注未验证**。

## 纪律补充

M1 不引入 RabbitMQ 客户端、不引入 Redis 客户端、不写履约消费与超时关单（均属 M2/M3）。任何偏离先更新本文件。

## M1 执行记录（2026-11-06）

- 任务 1.1–1.8 完成。**本机实测**：`dotnet build` 0 warning / 0 error；`dotnet test` **10 passed**（Domain 3 + Infrastructure 4 + Api 3）；`npm run build` 沿用 M0 结果仍通过。
- RED 证据 1：实现前 `CS0234` 找不到 `Pulse.Infrastructure.Orders`（退出码 1）；GREEN：4 个下单集成测试全绿。
- 产出：`src/Pulse.Infrastructure`（6 个实体、`PulseDbContext`、`PulseDbContextFactory`、`PlaceOrderService`）、`Migrations/20261106144831_InitialCreate.cs` 等 3 个迁移文件、`tests/Pulse.Infrastructure.Tests`、`tests/Pulse.Api.Tests/PlaceOrderEndpointTests.cs`、API 三个端点、compose 增加 `Database__Provider=MySql`。
- 本地默认 SQLite（`pulse-demo.db`，启动自动建表 + 播种 2 个 SKU），compose 覆盖为 MySQL 并走 `Migrate()`；`.tools/dotnet-ef` 为本地工具路径，未污染全局。
- **计划偏移（已记录）**：① 任务 1.7 原定手工 `curl` 验证，改为 `WebApplicationFactory` 自动化端点测试，更可重复；因共享本地 SQLite 库，`Pulse.Api.Tests` 加了 `CollectionBehavior(DisableTestParallelization = true)` 避免文件锁竞争。② 第 4 个并发测试改为「陈旧 version 更新触发 `DbUpdateConcurrencyException`」，比 SQLite 写锁竞争更稳定可重现。③ **纪律偏差**：API 薄适配层是「先实现后补集成测试」，未走严格 RED-GREEN；核心业务逻辑（Infrastructure 层）严格走了 TDD，**M2 起所有层恢复严格红绿**。
- **未验证**：MySQL 迁移应用到真实库、Redis、RabbitMQ、`docker compose up`（本机无 Docker）。
- M2 开始前需先把 M2 拆成 2–5 分钟粒度任务（outbox publisher + publisher confirms、RabbitMQ 拓扑、Redis 幂等短钥匙、Testcontainers 或本地服务验证策略）。
