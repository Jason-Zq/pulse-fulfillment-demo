# Pulse 公网部署指南（M7）

> 适用范围：把 Pulse 订单履约中台作为**作品集演示站**部署到一台可公网访问的服务器。
> 全部数据为**模拟数据**，页面与后台均标注「演示 · 非真实交易」。**这不是对外经营系统**。

---

## 0. 目标与边界（先读）

这个项目从选型起就明确：技术栈 `.NET 8 + Redis + RabbitMQ + MySQL + Vue3/Element Plus` 是为了**可被追问、有量化证据**的作品集，不是业务实用系统。因此：

- **支付是模拟的**：只有「模拟支付 · 演示」按钮，不接任何真实收银台 / 第三方支付。
- **数据是模拟的**：商品、库存、订单均为演示种子数据。
- **真实上线对外经营**在法律与资质上存在硬性门槛（支付资质、ICP 备案、**电商法**、**个人信息保护**、高可用与运维责任），且 **.NET 8 官方支持已于 2026-11-10 结束**。本指南只覆盖「可公网访问的演示站」，不涉及上述经营化改造。

### 本地已真实验证 / 仅作文档

| 项目 | 状态 |
| --- | --- |
| `docker compose up -d --build` 六服务全通 | ✅ 已真容器验证（开发基线） |
| 端到端链路 + 175 条断言（`--deep --m5 --experiments --m6 --m6c`） | ✅ 已真容器验证 |
| 浏览器逐页点击 A11（21/21） | ✅ 已真容器验证 |
| **`Swagger__Enabled=false` 关闭路径** | ✅ M7 已用生产覆盖干净重建真测（nginx 与 api 直连 `/swagger/...` 均 404） |
| 生产覆盖 `docker-compose.prod.yml` 收口（端口/口令/重启/资源） | ✅ 已真容器验证 |
| Caddy 边缘 TLS（真实域名 + ACME） | ⚠️ 需真实域名 / 公网 IP / 80·443 开放，本机仅做 `compose config` 语法校验，未跑真实证书签发 |

---

## 1. 前置条件

1. **一台公网服务器**：云主机（2 vCPU / 4 GB 起），已装 Docker Engine 与 Docker Compose v2。
2. **一个域名**：已把 A 记录解析到本机公网 IP（例如 `pulse.example.com`）。
3. **防火墙 / 安全组**：开放 **80** 与 **443** 入站（Caddy 用 80 做 ACME 挑战，443 提供 HTTPS）。**不要**把 3306/6379/5672/15672 开放到公网——生产覆盖已收回这些宿主端口。
4. **ICP 备案**：境内服务器对外提供 Web 服务需完成 ICP 备案（按云厂商流程）。
5. （可选）服务器若无法直连 `mcr.microsoft.com`，先拉取并打标签 .NET 镜像，见 §6 的 MCR 镜像坑。

---

## 2. 本地生产预览（无需域名，验证「收口」是否生效）

这一步在本机用生产覆盖起服务，**验证端口是否真的收回、Swagger 是否真的关闭、带口令的中间件链路是否仍跑得通**。无需真实域名。

```bash
# 1) 准备生产环境变量（填入强随机口令）
cp .env.prod.example .env.prod
#   编辑 .env.prod，把所有 __CHANGE_ME_...__ 替换为 openssl rand -base64 24 生成的随机串

# 2) 用「基线 + 生产覆盖」起服务（web 不再暴露宿主端口）
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build

# 3) 等健康（api 首次启动会跑 MySQL 迁移，约 10–30s）
docker compose -f docker-compose.prod.yml ps

# 4) 验证「宿主端口已收回」：以下端口应【都不监听】
for p in 3306 6379 5672 15672 5080 8080; do
  if nc -z localhost $p 2>/dev/null; then echo "FAIL: $p 仍暴露"; else echo "OK: $p 未暴露"; fi
done

# 5) 验证站点与 API（在 web 容器内经 nginx 反代访问，避免暴露宿主端口）
docker compose -f docker-compose.prod.yml exec web wget -qO- http://localhost/api/health
docker compose -f docker-compose.prod.yml exec web wget -qO- http://localhost/api/products

# 6) 验证 Swagger 已关闭（应返回 404，而非 SPA 回退的 200 HTML）
docker compose -f docker-compose.prod.yml exec web wget -qO- -S http://localhost/swagger/v1/swagger.json 2>&1 | head -n 5
#   → 期望 HTTP/1.1 404 Not Found

# 7) 验证带口令链路端到端跑通（可选）：重置实验后下一单，轮询至已发货
docker compose -f docker-compose.prod.yml exec web \
  wget -qO- --post-data='{"sku":"DEMO-001","quantity":1}' --header='Content-Type: application/json' \
  http://localhost/api/orders
```

> 预览结束清理：`docker compose -f docker-compose.prod.yml down -v`
> 想恢复可浏览的开发演示站：`docker compose up -d --build`（基线，端口照常暴露）。

---

## 3. 真实公网部署（Caddy 边缘 TLS）

```bash
# 在服务器上
git clone <你的仓库> pulse && cd pulse

cp .env.prod.example .env.prod        # 填强随机口令
export SITE_ADDRESS=pulse.example.com  # 改成你的真实域名

# 基线 + 生产覆盖 + 边缘代理，一次性起全部
docker compose -f docker-compose.prod.yml -f deploy/docker-compose.edge.yml \
  --env-file .env.prod up -d --build
```

- Caddy 会自动向 Let's Encrypt / ZeroSSL 申请并续期证书（需 80 入站可达完成 HTTP-01 挑战）。
- 仅 `caddy` 服务向宿主机发布 80/443；`web/api/mysql/redis/rabbitmq/worker` 全部只在内部网络。
- 访问 `https://pulse.example.com` 即可看到商城首页；后台入口在站内导航。

### 本地无域名自测边缘代理

```bash
SITE_ADDRESS=localhost docker compose -f docker-compose.prod.yml -f deploy/docker-compose.edge.yml --env-file .env.prod up -d --build
curl -kL https://localhost/        # Caddy 用本地 CA 签发，curl 需 -k
```

---

## 4. 密钥与配置安全

- `.env.prod` 已被 `.gitignore` 忽略（`!.env.example` 例外），**不会进仓库**。只用 `.env.prod.example` 作为模板。
- 所有口令用强随机值：`openssl rand -base64 24`。
- 生产覆盖已为 Redis 启用 `requirepass`、为 RabbitMQ 建立独立账号（取代默认 guest）、MySQL 用强 root 口令；连接串自动携带凭据。
- **公网部署前务必确认 `Swagger__Enabled=false`**（生产覆盖已默认注入；开发基线默认开启，切勿用基线直接上公网）。

---

## 5. 备份、监控、回滚

### 备份
- **MySQL**：定期 `docker compose exec mysql mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" pulse > pulse-$(date +%F).sql`。
- **卷**：`docker volume ls` 中 `pulse_mysql_data` / `caddy_data` 可用卷快照或 `docker run --rm -v pulse_mysql_data:/data -v $PWD:/backup busybox tar czf /backup/mysql.tgz /data`。

### 监控与日志
- 容器状态：`docker compose ps`；实时日志：`docker compose logs -f --tail=100 api worker`。
- 业务健康：`GET /api/health`（静态占位，仅说明 API 进程存活）；真实依赖状态看后台 `/admin/dashboard` 的 `source` 与 `/api/lab/state`。
- 队列深度在 `/admin/dashboard` 展示（依赖不可用时显示「—」，不伪造 0）。

### 回滚 / 升级
- 回滚：保留上一版镜像 tag，或 `git checkout <旧提交>` 后重新 `up -d --build`。
- 升级：拉新代码 → `docker compose ... up -d --build`；MySQL 迁移由 API 启动自动应用（首次会建表/迁移，注意备份）。

---

## 6. 风险清单（部署前逐条确认）

1. **.NET 8 已 EOL（2026-11-10）**：本地 / CI / 演示不受影响；若要做长期在线 demo，建议评估升 .NET 10（本项目**未擅自升级**，需单独评估）。
2. **支付资质 / 电商法 / 个人信息保护**：演示站全部模拟数据、明确标注非真实交易，规避经营化合规风险；若要对外经营需另行合规改造。
3. **高可用与运维责任**：单节点 `restart: unless-stopped` 仅能自愈进程崩溃，不提供跨机高可用；演示站可接受，生产经营系统需额外架构。
4. **中间件加固**：生产覆盖已收口端口 + 加口令；但**仍建议**在云安全组层面再次确认 3306/6379/5672/15672 不对公网开放。
5. **Swagger 暴露**：公网前必须 `Swagger__Enabled=false`（生产覆盖已设；开发基线默认开）。
6. **MCR 镜像拉取坑**：部分环境直连 `mcr.microsoft.com` 极慢。可先在能直连的机器：
   ```bash
   docker pull mcr.m.daocloud.io/dotnet/sdk:8.0
   docker pull mcr.m.daocloud.io/dotnet/aspnet:8.0
   docker tag  mcr.m.daocloud.io/dotnet/sdk:8.0    mcr.microsoft.com/dotnet/sdk:8.0
   docker tag  mcr.m.daocloud.io/dotnet/aspnet:8.0 mcr.microsoft.com/dotnet/aspnet:8.0
   docker save mcr.microsoft.com/dotnet/sdk:8.0 mcr.microsoft.com/dotnet/aspnet:8.0 | \
     docker -H <目标机> load
   ```
   其余镜像（mysql / redis / rabbitmq / nginx / caddy / node）走 Docker Hub 即可。
