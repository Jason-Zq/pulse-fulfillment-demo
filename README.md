# Pulse · 订单履约中台（作品集演示站）

> **演示环境 · 全部数据为模拟 · 不涉及真实交易。本项目不是生产系统。**
> 边界与未实现能力见 [`docs/BOUNDARIES.md`](docs/BOUNDARIES.md)。

一个用来把 MySQL / Redis / RabbitMQ 讲透的全栈作品集项目：完整订单履约链路 + Element Plus 运营后台 + 三个可复现实验。正式规格见 [`docs/SPEC.md`](docs/SPEC.md)，实施计划见 [`docs/PLAN.md`](docs/PLAN.md)。

## 技术栈

| 层 | 选型 |
|---|---|
| 后端 | .NET 8 / ASP.NET Core（C#） |
| 存储 | MySQL 8 |
| 缓存 | Redis 7 |
| 消息 | RabbitMQ 3.13（TTL+DLX 做超时关单） |
| 前端 | Vue 3 + TypeScript + Vite + Element Plus |
| 编排 | Docker Compose |

## 当前完成度：M0（骨架）

已具备：.NET 8 解决方案（Domain / Api / Worker / Domain.Tests）、订单状态机及其红绿 TDD 测试、`GET /api/health`、Vue + Element Plus 前端骨架、五服务 compose 编排、边界说明文档。

尚未实现：下单、outbox、消息拓扑、履约消费、超时关单、死信补偿、实验台、压测报告、演示 GIF。按 [`docs/PLAN.md`](docs/PLAN.md) 的 M1–M6 推进。

## 启动

**方式 A：一键启动（推荐）**

```bash
cp .env.example .env
docker compose up -d
# 前端 http://localhost:8080  API http://localhost:5080/api/health  RabbitMQ 管理台 http://localhost:15672
```

> 注意：开发机当前**未安装 Docker**，因此 `docker compose up` 尚未在本机实际验证过。compose 文件按规格写全，首次在有 Docker 的机器上运行时请核对镜像拉取与 healthcheck。

**方式 B：不用 Docker 的本地开发**（需要本机另外提供 MySQL / Redis / RabbitMQ）

```bash
dotnet run --project src/Pulse.Api           # http://localhost:5080
cd web && npm install && npm run dev         # http://localhost:5173，/api 已代理到 5080
```

## 已实测的验证结果

| 命令 | 结果 |
|---|---|
| `dotnet build src/Pulse.sln` | 通过，0 warning / 0 error |
| `dotnet test`（RED，实现前） | 失败：`CS0103` 找不到 `OrderStateMachine` / `OrderStatus` |
| `dotnet test`（GREEN，实现后） | 通过：3 个订单状态机测试 + 1 个健康检查集成测试，共 4 passed |
| `npm --prefix web install` | 通过，111 个包 |
| `npm --prefix web run build` | 通过，`vue-tsc` 类型检查 + Vite 构建成功（仅有 chunk >500 kB 体积提示，非错误） |
| `docker compose up -d` | **本机无 Docker，未验证**（见 `docs/BOUNDARIES.md` 第 7 节） |

未跑过的验证一律不写「通过」；后续里程碑的结果会继续追加到这张表。

## 设计取舍（摘要）

- **outbox + publisher confirms**：解决「DB 写成功但 MQ 没发出去」，配合消费端幂等形成不丢不重闭环。
- **TTL+DLX 而非延迟消息插件**：演示超时时长固定，`docker compose` 少一个插件依赖。
- **3 秒轮询而非 SignalR**：后台是低频查看页面，长连接属于过度设计；浏览器不直连消息总线。
- **RabbitMQ 而非 Kafka**：本项目要的是任务队列的重试 / 死信 / 逐条确认语义，不是日志重放。

完整取舍记录见 `docs/BOUNDARIES.md` 第 6 节。

## 文档索引

| 文件 | 内容 |
|---|---|
| `docs/SPEC.md` | 已确认的正式规格（目标、数据模型、消息拓扑、验收 A1–A10、里程碑） |
| `docs/PLAN.md` | 实施计划：M0 的 2–5 分钟粒度任务 + M1–M6 路线图 |
| `docs/BOUNDARIES.md` | 边界说明：演示声明、未实现能力、上线还差什么 |
| `docs/design.md` | 选型讨论历史（已由 SPEC 取代） |
| `docs/archive/` | 更早的实时大屏草案存档，非规格 |
