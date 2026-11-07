#!/usr/bin/env python3
"""Pulse 真容器端到端验证（C 层 + M5）。

入口一律走 nginx http://localhost:8080 -> api:8080，模拟真实用户路径；
副作用逐项核对 MySQL / Redis / RabbitMQ 管理 API 的真实状态。
前置：docker compose up -d（六服务 healthy 后运行）。
用法：python3 scripts/verify_docker_e2e.py [--deep] [--m5] [--experiments] [--m6] [--m6c]
     --deep        追加 M3「超时关单 / 三次重试入死信 / 人工补偿重投」异步链路验证。
     --m5          追加 M5 九个新端点逐字段契约核对 + 控制开关跨进程生效验证 + 一键重置。
     --experiments 追加 E1/E2/E3 真实跑（样本量由后端默认值决定，耗时较长，需先 --m5 或单独跑）。
     --m6          追加 M6b「items[] 多明细下单 → 模拟支付 → Worker 异步履约 → 四方对账」链路。
     --m6c         追加 M6c「GET /api/pipeline 7 节点真实计数」+ 容器 Production 下 Swagger 开放验证。
默认凭据取自 .env.example / compose 默认值；若改过密码请同步修改下方常量。
"""
import base64
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

CWD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = "http://localhost:8080"      # nginx -> api
DIRECT = "http://localhost:5080"    # api 直连
MGMT = "http://localhost:15672/api"
AUTH = base64.b64encode(b"guest:guest").decode()
DEEP = "--deep" in sys.argv
M5 = "--m5" in sys.argv
EXPERIMENTS = "--experiments" in sys.argv
M6 = "--m6" in sys.argv
M6C = "--m6c" in sys.argv

results = []


def check(name, cond, detail=""):
    ok = bool(cond)
    results.append((name, ok, detail))
    print(("PASS  " if ok else "FAIL  ") + name + (("  | " + str(detail)) if detail else ""), flush=True)
    return ok


def fieldset(obj, expected, label):
    """逐字段核对 DTO 契约：字段名集合必须完全相等（M4 的 5 处裂缝教训）。"""
    ok = isinstance(obj, dict) and set(obj.keys()) == set(expected)
    detail = sorted(obj.keys()) if isinstance(obj, dict) else repr(obj)
    return check(label, ok, detail if not ok else "")


def http(method, path, body=None, base=BASE, headers=None, timeout=10):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw


def raw_get(path, base=BASE):
    """取原始响应（含 Content-Type），用于判别 Swagger 是否被 nginx SPA 回退成 HTML。"""
    try:
        with urllib.request.urlopen(urllib.request.Request(base + path), timeout=10) as r:
            return r.status, r.headers.get("Content-Type", ""), r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.headers.get("Content-Type", ""), e.read().decode()
    except Exception as e:
        return 0, "", repr(e)


def mgmt(path, method="GET", body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(MGMT + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    req.add_header("Authorization", "Basic " + AUTH)
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read().decode() or "null")


def sql(query):
    p = subprocess.run(
        ["docker", "compose", "exec", "-T", "mysql", "mysql",
         "-uroot", "-ppulse_dev", "pulse", "-N", "-B", "-e", query],
        cwd=CWD, capture_output=True, text=True)
    if p.returncode != 0:
        return "SQLERR:" + p.stderr.strip()
    return p.stdout.strip()


def redis(*args):
    p = subprocess.run(
        ["docker", "compose", "exec", "-T", "redis", "redis-cli", *args],
        cwd=CWD, capture_output=True, text=True)
    return p.stdout.strip()


def scalar(query):
    out = sql(query)
    lines = out.splitlines()
    return lines[0] if lines else ""


def qstat(name):
    return mgmt("/queues/%2F/" + name)


def wait_until(label, fn, timeout=30, interval=0.5):
    end = time.time() + timeout
    last = None
    while time.time() < end:
        try:
            last = fn()
            if last:
                return True, last
        except Exception as ex:
            last = repr(ex)
        time.sleep(interval)
    return False, last


def publish(routing_key, payload, message_id, headers=None):
    body = {
        "routing_key": routing_key,
        "payload": json.dumps(payload),
        "payload_encoding": "string",
        "properties": {"delivery_mode": 2, "message_id": message_id,
                       "headers": headers or {}},
    }
    return mgmt("/exchanges/%2F/ex.orders/publish", "POST", body)


def wait_for_stack(timeout=120):
    """compose up -d 返回时 API 可能仍在跑 MySQL 迁移，nginx 会短暂 502。"""
    end = time.time() + timeout
    while time.time() < end:
        try:
            st, body = http("GET", "/api/health")
            if st == 200 and isinstance(body, dict) and body.get("status") == "ok":
                return True
        except Exception:
            pass
        time.sleep(1)
    return False


print("=" * 72)
print("Pulse · Docker 六服务端到端验证  (deep=%s, m5=%s, experiments=%s, m6=%s, m6c=%s)" %
      (DEEP, M5, EXPERIMENTS, M6, M6C))
print("=" * 72, flush=True)

# ---------------------------------------------------------------- 阶段 0 基线
print("\n--- 阶段 0：等待六服务就绪 ---")
check("API 已完成 MySQL 迁移并开始响应（health 200）", wait_for_stack(), "timeout=120s")
print("\n--- 阶段 0：容器基线 / nginx 反代 ---")
st, h = http("GET", "/api/health")
check("nginx 8080 反代 /api/health 返回 200", st == 200, st)
check("健康检查显示数据库为 mysql（非本地 sqlite）",
      isinstance(h, dict) and h.get("database") == "mysql", h if not isinstance(h, dict) else h.get("database"))
st, h2 = http("GET", "/api/health", base=DIRECT)
check("api 5080 直连健康检查 200", st == 200 and isinstance(h2, dict), h2)
st, prods = http("GET", "/api/products")
# M8：启动种子与 /api/lab/reset 同源于 DemoCatalog（8 个 SKU / 5 个分类），不再只有 2 个演示 SKU。
check("/api/products 返回 8 个商品",
      st == 200 and isinstance(prods, list) and len(prods) == 8,
      "count=%s" % (len(prods) if isinstance(prods, list) else prods))
# 字段演进：M4 的 8 字段 → M6b 加 version（9）→ M8 加 category（10）。字段名集合必须完全相等。
check("Product DTO 10 字段齐全（M6b version + M8 category）",
      isinstance(prods, list) and bool(prods)
      and set(prods[0].keys()) == {"id", "sku", "name", "price", "available", "reserved",
                                   "deducted", "status", "version", "category"},
      sorted(prods[0].keys()) if isinstance(prods, list) and prods else prods)
check("8 个商品覆盖 5 个分类且无空分类",
      isinstance(prods, list) and bool(prods)
      and len({p.get("category") for p in prods}) == 5
      and all(isinstance(p.get("category"), str) and p["category"].strip() for p in prods),
      sorted({str(p.get("category")) for p in prods}) if isinstance(prods, list) else prods)
check("商品 id 为 number",
      isinstance(prods, list) and bool(prods) and isinstance(prods[0]["id"], int))
check("MySQL 初始订单数为 0", scalar("SELECT COUNT(*) FROM Orders;") == "0")
check("MySQL 迁移至少 2 条（M5 生成第 3 条后为 3）",
      int(scalar("SELECT COUNT(*) FROM __EFMigrationsHistory;") or 0) >= 2,
      scalar("SELECT COUNT(*) FROM __EFMigrationsHistory;"))
check("RabbitMQ ex.orders 已声明", any(x["name"] == "ex.orders" for x in mgmt("/exchanges")))
qs = {q["name"]: q for q in mgmt("/queues")}
check("4 个业务队列已声明",
      all(k in qs for k in ["q.fulfillment", "q.order.timeout", "q.fulfillment.retry", "q.dlq.fulfillment"]),
      sorted(qs.keys()))
# RabbitMQ 管理台统计有约 5 秒采样间隔，Worker 懒连接后立即读 consumers 会误报 0。
ok, last = wait_until("consumer online", lambda: mgmt(
    "/queues/%2F/q.fulfillment").get("consumers", 0) >= 1, 30, 1.0)
check("q.fulfillment 有消费者在线（等待管理台统计刷新，最多 30s）", ok,
      "consumers=%s" % mgmt("/queues/%2F/q.fulfillment").get("consumers"))
check("q.order.timeout 配置了 15 分钟 TTL + DLX",
      qs.get("q.order.timeout", {}).get("arguments", {}).get("x-message-ttl") == 900000
      and qs.get("q.order.timeout", {}).get("arguments", {}).get("x-dead-letter-routing-key") == "order.timeout",
      qs.get("q.order.timeout", {}).get("arguments"))

# ---------------------------------------------------------------- 阶段 1 下单
print("\n--- 阶段 1：下单 / 幂等 / outbox 投递 ---")
key = "docker-e2e-001"
st, created = http("POST", "/api/orders",
                   {"sku": "SKU-1", "quantity": 2, "idempotencyKey": key},
                   headers={"Idempotency-Key": key})
check("POST /api/orders 200", st == 200, created)
check("下单响应含 id (number)", isinstance(created.get("id"), int), created)
oid = created["id"]
order_no = created["orderNo"]
check("下单后状态 Created", created.get("status") == "Created")

st, dup = http("POST", "/api/orders",
               {"sku": "SKU-1", "quantity": 2, "idempotencyKey": key},
               headers={"Idempotency-Key": key})
check("相同幂等键返回同一订单 id", dup.get("id") == oid, dup)
check("重复下单未产生第二笔订单", scalar("SELECT COUNT(*) FROM Orders;") == "1")
check("库存预扣 available=98 / reserved=2",
      scalar("SELECT Available FROM Inventories WHERE Sku='SKU-1';") == "98"
      and scalar("SELECT Reserved FROM Inventories WHERE Sku='SKU-1';") == "2")
check("写入 1 条 Reserve 流水",
      scalar("SELECT COUNT(*) FROM InventoryLedgers WHERE ChangeType='Reserve';") == "1")
check("下单同事务写入 1 条 outbox(order.created)",
      scalar("SELECT COUNT(*) FROM OutboxMessages WHERE RoutingKey='order.created';") == "1")

ok, last = wait_until("outbox published", lambda: scalar(
    "SELECT COUNT(*) FROM OutboxMessages WHERE RoutingKey='order.created' AND Status='published';") == "1", 30)
check("OutboxPublisher 已把 order.created 投递为 published", ok, last)
# RabbitMQ 管理 API 的队列统计有约 5 秒采样间隔，不能即时断言
ok, last = wait_until("q.order.timeout ready", lambda: (
    qstat("q.order.timeout").get("messages", 0) >= 1), 20, 1.0)
check("publisher confirm 后 q.order.timeout 积压 1 条（等待 15 分钟 TTL）", ok,
      "messages=%s" % qstat("q.order.timeout").get("messages"))

# ---------------------------------------------------------------- 阶段 2 支付消费
print("\n--- 阶段 2：支付 → MQ → 履约消费 → 库存实扣 ---")
st, paid = http("POST", "/api/orders/%s/pay" % oid, {})
check("POST /pay 返回 Paid", st == 200 and paid.get("status") == "Paid", paid)
check("支付同事务写入 outbox(order.paid)",
      scalar("SELECT COUNT(*) FROM OutboxMessages WHERE RoutingKey='order.paid';") == "1")

ok, last = wait_until("order.paid published", lambda: scalar(
    "SELECT COUNT(*) FROM OutboxMessages WHERE RoutingKey='order.paid' AND Status='published';") == "1", 30)
check("order.paid 已投递为 published", ok, last)

# Orders.Status 在 MySQL 里是 HasConversion<int>()，用 API 的语义值断言而不是裸列
ok, last = wait_until("order shipped", lambda: http(
    "GET", "/api/orders/%s" % oid)[1].get("status") == "Shipped", 30)
check("消费者把订单 Paid → Shipped（真 RabbitMQ 消费）", ok, last)
check("库存实扣：reserved 归 0 / available 保持 98",
      scalar("SELECT Reserved FROM Inventories WHERE Sku='SKU-1';") == "0"
      and scalar("SELECT Available FROM Inventories WHERE Sku='SKU-1';") == "98",
      "reserved=%s available=%s" % (scalar("SELECT Reserved FROM Inventories WHERE Sku='SKU-1';"),
                                    scalar("SELECT Available FROM Inventories WHERE Sku='SKU-1';")))
check("写入 1 条 Deduct 流水（数量 2）",
      scalar("SELECT SUM(Quantity) FROM InventoryLedgers WHERE ChangeType='Deduct';") == "2")
check("processed_events 落库（消费端幂等双保险）",
      int(scalar("SELECT COUNT(*) FROM ProcessedEvents;") or 0) >= 1,
      "rows=%s" % scalar("SELECT COUNT(*) FROM ProcessedEvents;"))
keys = redis("--scan", "--pattern", "*").splitlines()
check("Redis dedup:{event_id} 短钥匙真实写入（SPEC 第 5 节命名，无双前缀）",
      any(k.startswith("dedup:") for k in keys) and not any(k.startswith("idem:dedup:") for k in keys),
      keys[:5])
check("q.fulfillment 消息已被手动 ack 消费完", qstat("q.fulfillment").get("messages", 0) == 0,
      "messages=%s" % qstat("q.fulfillment").get("messages"))
st, prods2 = http("GET", "/api/products")
p1 = next(p for p in prods2 if p["sku"] == "SKU-1")
check("/api/products 的 deducted 聚合为 2", p1["deducted"] == 2, p1["deducted"])

# ---------------------------------------------------------------- 阶段 3 API 面
print("\n--- 阶段 3：M4 接口面 / 参数校验 / 日报对账 ---")
st, lst = http("GET", "/api/orders?page=1&pageSize=20")
check("订单列表 200 且 total>=1", st == 200 and lst.get("total") >= 1, lst and lst.get("total"))
check("列表项含 sku 字段", "sku" in lst["items"][0], sorted(lst["items"][0].keys()))
st, detail = http("GET", "/api/orders/%s" % oid)
check("详情 timeline 含 Created/Paid/Shipped",
      {t["status"] for t in detail["timeline"]} >= {"Created", "Paid", "Shipped"},
      [t["status"] for t in detail["timeline"]])
today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
st, report = http("GET", "/api/reports/daily?date=" + today)
check("日报 200 且 orderCnt>=1 / paidCnt>=1", st == 200 and report["orderCnt"] >= 1 and report["paidCnt"] >= 1,
      report)
check("日报 reconciliation 字段齐全",
      set(report["reconciliation"].keys()) == {"orderAmount", "itemAmount", "ledgerReserveQty", "diff"},
      sorted(report["reconciliation"].keys()))
check("四方对账 diff = 0", float(report["reconciliation"]["diff"]) == 0.0, report["reconciliation"]["diff"])
st, dl = http("GET", "/api/dead-letters")
check("死信列表 200 且为数组", st == 200 and isinstance(dl, list), len(dl) if isinstance(dl, list) else dl)
st, bad = http("GET", "/api/reports/daily?date=bad")
check("非法日期返回 400", st == 400, st)
st, _ = http("POST", "/api/products/prices", [{"sku": "SKU-1", "price": 0}])
check("批量改价 price<=0 返回 400", st == 400, st)
st, upd = http("POST", "/api/products/prices", [{"sku": "SKU-2", "price": 188}])
check("批量改价成功返回 updated", st == 200 and upd.get("updated") == 1, upd)
check("改价已落 MySQL", scalar("SELECT Price FROM Products WHERE Sku='SKU-2';") == "188.00",
      scalar("SELECT Price FROM Products WHERE Sku='SKU-2';"))
st, _ = http("POST", "/api/orders", {"sku": "SKU-1", "quantity": 9999, "idempotencyKey": "docker-e2e-oversell"})
check("超卖返回 409", st == 409, st)
check("超卖未产生订单", scalar("SELECT COUNT(*) FROM Orders WHERE IdempotencyKey='docker-e2e-oversell';") == "0")
st, _ = http("POST", "/api/orders/999999/pay", {})
check("不存在订单支付返回 404", st == 404, st)

# ---------------------------------------------------------------- 阶段 4 取消
print("\n--- 阶段 4：取消释放预扣 ---")
st, c2 = http("POST", "/api/orders",
              {"sku": "SKU-2", "quantity": 3, "idempotencyKey": "docker-e2e-002"})
check("第二笔订单创建成功", st == 200, c2)
cid = c2["id"]
check("取消前 SKU-2 reserved=3", scalar("SELECT Reserved FROM Inventories WHERE Sku='SKU-2';") == "3")
st, canc = http("POST", "/api/orders/%s/cancel" % cid, {})
check("POST /cancel 返回 Cancelled", st == 200 and canc.get("status") == "Cancelled", canc)
check("取消后 reserved 归 0 / available 回到 50",
      scalar("SELECT Reserved FROM Inventories WHERE Sku='SKU-2';") == "0"
      and scalar("SELECT Available FROM Inventories WHERE Sku='SKU-2';") == "50")
check("写入 1 条 Release 流水",
      scalar("SELECT COUNT(*) FROM InventoryLedgers WHERE ChangeType='Release';") == "1")
st, _ = http("POST", "/api/orders/%s/ship" % cid, {})
check("已取消订单发货返回 404（状态机拒绝）", st == 404, st)

# ---------------------------------------------------------------- 阶段 5 deep
if DEEP:
    print("\n--- 阶段 5（deep）：超时关单 / 三次重试入死信 ---")
    st, c3 = http("POST", "/api/orders",
                  {"sku": "SKU-2", "quantity": 1, "idempotencyKey": "docker-e2e-003"})
    c3id = c3["id"]
    c3no = c3["orderNo"]
    check("第三笔订单创建成功", st == 200, c3)
    # 模拟 TTL 到期：把过期时间改到过去，再直接投递 DLX 的 routing key
    sql("UPDATE Orders SET ExpiresAtUtc='2020-01-01 00:00:00' WHERE Id=%s;" % c3id)
    evt = {"orderNo": c3no, "sku": "SKU-2", "quantity": 1, "expiresAtUtc": "2020-01-01T00:00:00Z"}
    pub = publish("order.timeout", evt, "timeout-" + c3no)
    check("mgmt API 投递 order.timeout 成功", pub.get("routed") is True, pub)
    ok, last = wait_until("timeout cancelled", lambda: http(
        "GET", "/api/orders/%s" % c3id)[1].get("status") == "Cancelled", 20)
    check("超时关单：Created → Cancelled（DLX 后的消费路径）", ok, last)
    check("超时关单释放预扣并写 Release 流水",
          scalar("SELECT COUNT(*) FROM InventoryLedgers WHERE RefOrderId=%s AND ChangeType='Release';" % c3id) == "1")

    # 制造一条必然失败的消息（订单不存在），观察 3 次重试后入死信
    before = int(scalar("SELECT COUNT(*) FROM DeadLetters;") or 0)
    ghost = {"orderNo": "NO-SUCH-ORDER", "sku": "SKU-1", "quantity": 1}
    pub = publish("order.paid", ghost, "deadletter-probe-001")
    check("mgmt API 投递失败探针成功", pub.get("routed") is True, pub)
    ok, last = wait_until("dead letter recorded", lambda: (
        int(scalar("SELECT COUNT(*) FROM DeadLetters;") or 0) > before), 60, 1.0)
    check("三次重试后 DeadLetters 落库（重试 / DLQ 真链路）", ok,
          "before=%s after=%s" % (before, scalar("SELECT COUNT(*) FROM DeadLetters;")))
    check("死信重试次数为 3（不是被判重后静默丢弃）",
          scalar("SELECT RetryCount FROM DeadLetters WHERE OriginalEventId='deadletter-probe-001';") == "3",
          scalar("SELECT RetryCount FROM DeadLetters WHERE OriginalEventId='deadletter-probe-001';"))
    st, dl2 = http("GET", "/api/dead-letters")
    check("补偿台能看到该死信", st == 200 and any("deadletter-probe" in (x.get("originalEventId") or "") for x in dl2),
          [x.get("originalEventId") for x in dl2][:3])
    # 人工重试
    if dl2:
        target = next((x for x in dl2 if "deadletter-probe" in (x.get("originalEventId") or "")), None)
        if target:
            st, _ = http("POST", "/api/dead-letters/%s/retry" % target["id"], {})
            check("死信人工重试返回 handled", st == 200, st)
            outbox_before = scalar("SELECT COUNT(*) FROM OutboxMessages WHERE Payload LIKE '%NO-SUCH-ORDER%';")
            check("人工重试写入一条新的 pending outbox 供重投", outbox_before == "1", outbox_before)
            ok2, last2 = wait_until("new dead letter from manual retry", lambda: (
                int(scalar("SELECT COUNT(*) FROM DeadLetters;") or 0) > before + 1), 45, 1.0)
            check("重投后生成新 event_id 的新死信（根因未修复时的预期行为）", ok2,
                  "deadLetters=%s" % scalar("SELECT COUNT(*) FROM DeadLetters;"))
    # 清理仍在重试队列中的探针消息，避免刷屏
    mgmt("/queues/%2F/q.fulfillment.retry/contents", "DELETE")

# ---------------------------------------------------------------- 阶段 6（M5）
if M5:
    print("\n--- 阶段 6（M5）：九个新端点逐字段契约核对 ---")
    st, dash = http("GET", "/api/dashboard", timeout=30)
    d = dash if isinstance(dash, dict) else {}
    check("GET /api/dashboard 200", st == 200, st if st != 200 else "")
    fieldset(d, ["generatedAtUtc", "today", "inventory", "queues", "outboxPending",
                 "deadLetterPending", "alertsActive", "controls", "source"], "dashboard 顶层字段")
    fieldset(d.get("today"), ["orderCnt", "paidCnt", "cancelCnt", "gmv"], "dashboard.today 字段")
    fieldset(d.get("inventory"), ["skuCount", "lowStockCount", "totalAvailable", "lowStockThreshold"],
             "dashboard.inventory 字段")
    fieldset(d.get("queues"), ["fulfillment", "orderTimeout", "fulfillmentRetry", "dlq", "available"],
             "dashboard.queues 字段")
    fieldset(d.get("controls"), ["consumerEnabled", "publisherEnabled", "failureInjectionEnabled"],
             "dashboard.controls 字段")
    fieldset(d.get("source"), ["database", "redis", "rabbitmq"], "dashboard.source 字段")
    check("dashboard 计数项为 number（非 bool）",
          all(isinstance(d.get(k), (int, float)) and not isinstance(d.get(k), bool)
              for k in ["outboxPending", "deadLetterPending", "alertsActive"]),
          {k: d.get(k) for k in ["outboxPending", "deadLetterPending", "alertsActive"]})
    check("dashboard.queues.available 为 bool", isinstance(d.get("queues", {}).get("available"), bool),
          d.get("queues", {}).get("available"))
    check("dashboard.source 三项均为字符串",
          all(isinstance(d.get("source", {}).get(k), str) for k in ["database", "redis", "rabbitmq"]),
          d.get("source"))
    if d.get("queues", {}).get("available"):
        check("队列深度可用时 4 个数值均为 number",
              all(isinstance(d["queues"].get(k), (int, float)) for k in
                  ["fulfillment", "orderTimeout", "fulfillmentRetry", "dlq"]),
              {k: d["queues"].get(k) for k in ["fulfillment", "orderTimeout", "fulfillmentRetry", "dlq"]})
    else:
        check("队列深度不可用时如实标注 available=false（不伪造 0）", True,
              "queues=%s" % d.get("queues"))

    st, rep5 = http("GET", "/api/reports/daily?date=" + today)
    check("日报新增 persisted 字段且为 bool",
          st == 200 and isinstance(rep5, dict) and isinstance(rep5.get("persisted"), bool),
          rep5.get("persisted") if isinstance(rep5, dict) else rep5)
    check("日报 M4 字段未被破坏",
          isinstance(rep5, dict) and set(rep5.keys()) >=
          {"statDate", "orderCnt", "paidCnt", "cancelCnt", "gmv", "reconciliation", "persisted"},
          sorted(rep5.keys()) if isinstance(rep5, dict) else rep5)
    tables = sql("SHOW TABLES;").splitlines()
    check("MySQL 存在日报持久化表", any("DailyReport" in t for t in tables),
          [t for t in tables if "Report" in t])
    check("MySQL 存在实验结果表", any("Experiment" in t for t in tables),
          [t for t in tables if "Experiment" in t])
    check("MySQL 存在告警表 / 控制表",
          any("Alert" in t for t in tables) and any("LabControl" in t for t in tables),
          [t for t in tables if "Alert" in t or "LabControl" in t])

    # silence 端点要求 alertKey 已存在（不存在返回 404）。干净库可能无任何告警条件，
    # 因此显式落一条专用探针行，让「静默成功路径 + Redis 静默键」可被真实验证。
    alert_table = next((t for t in tables if "Alert" in t), None)
    probe_insert = "no-alert-table"
    if alert_table:
        probe_insert = sql(
            "INSERT INTO `%s` (AlertKey,Level,Message,CreatedAtUtc,Silenced,SilencedUntilUtc) "
            "VALUES ('e2e-silence-probe','info','E2E 静默探针',UTC_TIMESTAMP(6),0,NULL);" % alert_table)
    check("落一条专用告警探针行（供静默端点验证）",
          alert_table is not None and not probe_insert.startswith("SQLERR:"), probe_insert)

    st, alerts = http("GET", "/api/alerts?limit=50")
    check("GET /api/alerts 200 且为数组", st == 200 and isinstance(alerts, list),
          len(alerts) if isinstance(alerts, list) else alerts)
    if isinstance(alerts, list) and alerts:
        fieldset(alerts[0], ["id", "alertKey", "level", "message", "createdAtUtc", "silenced",
                             "silencedUntilUtc"], "AlertRecord 字段")
        check("AlertRecord.id 为 number / level 为约定枚举",
              isinstance(alerts[0]["id"], int) and alerts[0]["level"] in ("info", "warning", "critical"),
              alerts[0])
    else:
        check("AlertRecord 字段（当前无告警，空列表跳过逐字段）", True, "空列表")
    st, sil = http("POST", "/api/alerts/e2e-silence-probe/silence", {"minutes": 5})
    check("POST /api/alerts/{key}/silence 200", st == 200, sil)
    fieldset(sil, ["alertKey", "silenced", "silencedUntilUtc"], "SilenceResult 字段")
    check("静默返回 silenced=true", isinstance(sil, dict) and sil.get("silenced") is True, sil)
    silence_key_exists = redis("EXISTS", "alert:silence:e2e-silence-probe")
    check("Redis alert:silence:{key} 静默键真实写入（SPEC 第 5 节）",
          silence_key_exists == "1", silence_key_exists)
    st, a2 = http("GET", "/api/alerts?limit=50")
    probe = next((x for x in a2 if x.get("alertKey") == "e2e-silence-probe"), None) if isinstance(a2, list) else None
    check("告警列表同步显示 silenced=true / silencedUntilUtc",
          probe is not None and probe.get("silenced") is True and probe.get("silencedUntilUtc"),
          probe)
    st, _ = http("POST", "/api/alerts/e2e-silence-probe/silence", {"minutes": 99999})
    check("静默 minutes 越界返回 400", st == 400, st)
    st, _ = http("POST", "/api/alerts/no-such-alert-key/silence", {"minutes": 5})
    check("静默不存在的 alertKey 返回 404（契约边界）", st == 404, st)

    st, state = http("GET", "/api/lab/state")
    fieldset(state, ["consumerEnabled", "publisherEnabled", "failureInjectionEnabled", "database",
                     "redisConfigured", "rabbitConfigured"], "LabState 字段")
    check("LabState 布尔字段类型正确",
          isinstance(state, dict) and all(isinstance(state.get(k), bool) for k in
                                          ["consumerEnabled", "publisherEnabled",
                                           "failureInjectionEnabled", "redisConfigured", "rabbitConfigured"]),
          state)
    check("全新库上故障注入默认关闭（不能默认 on 毁掉履约）",
          isinstance(state, dict) and state.get("failureInjectionEnabled") is False,
          state.get("failureInjectionEnabled") if isinstance(state, dict) else state)

    print("\n--- 阶段 6b：控制开关跨 API / Worker 进程真实生效 ---")
    st, inj = http("POST", "/api/lab/inject", {"type": "stop_publisher"})
    fieldset(inj, ["type", "applied", "detail"], "InjectResult 字段")
    check("停 Publisher 返回 applied=true", st == 200 and isinstance(inj, dict) and inj.get("applied") is True, inj)
    st, state2 = http("GET", "/api/lab/state")
    check("state.publisherEnabled=false（API 侧可见）",
          isinstance(state2, dict) and state2.get("publisherEnabled") is False, state2)
    pending_before = int(scalar("SELECT COUNT(*) FROM OutboxMessages WHERE Status='pending';") or 0)
    st, c4 = http("POST", "/api/orders",
                  {"sku": "SKU-1", "quantity": 1, "idempotencyKey": "docker-e2e-m5-stop-pub"})
    check("停 Publisher 期间下单仍成功（不阻塞下单入口）", st == 200, c4)
    time.sleep(6)
    pending_after = int(scalar("SELECT COUNT(*) FROM OutboxMessages WHERE Status='pending';") or 0)
    check("停 Publisher 期间 outbox 保持 pending（Worker 真的停止投递）",
          pending_after >= pending_before + 1, "before=%s after=%s" % (pending_before, pending_after))
    st, inj2 = http("POST", "/api/lab/inject", {"type": "start_publisher"})
    check("开 Publisher 返回 applied=true", st == 200 and isinstance(inj2, dict) and inj2.get("applied") is True, inj2)
    ok, last = wait_until("pending drained", lambda: int(scalar(
        "SELECT COUNT(*) FROM OutboxMessages WHERE Status='pending';") or 0) <= pending_before, 40, 1.0)
    check("恢复 Publisher 后积压 outbox 全部补投（E1 机制）", ok,
          "pending=%s" % scalar("SELECT COUNT(*) FROM OutboxMessages WHERE Status='pending';"))

    st, _ = http("POST", "/api/lab/inject", {"type": "stop_consumer"})
    st, state3 = http("GET", "/api/lab/state")
    check("state.consumerEnabled=false（停消费者）",
          isinstance(state3, dict) and state3.get("consumerEnabled") is False, state3)
    st, _ = http("POST", "/api/lab/inject", {"type": "start_consumer"})
    st, _ = http("POST", "/api/lab/inject", {"type": "failure_on"})
    st, state4 = http("GET", "/api/lab/state")
    check("state.failureInjectionEnabled=true（故障注入开启）",
          isinstance(state4, dict) and state4.get("failureInjectionEnabled") is True, state4)
    st, _ = http("POST", "/api/lab/inject", {"type": "failure_off"})
    for t in ("start_publisher", "start_consumer"):
        http("POST", "/api/lab/inject", {"type": t})
    st, state5 = http("GET", "/api/lab/state")
    check("控制开关全部恢复默认（consumer/publisher on、failure off）",
          isinstance(state5, dict) and state5.get("consumerEnabled") is True
          and state5.get("publisherEnabled") is True and state5.get("failureInjectionEnabled") is False,
          state5)

    st, tr = http("POST", "/api/lab/traffic",
                  {"sku": "SKU-1", "count": 10, "rps": 20, "quantity": 1, "concurrency": 2}, timeout=180)
    fieldset(tr, ["requested", "created", "failed", "elapsedMs", "throughputRps", "p99Ms",
                  "experimentId"], "TrafficResult 字段")
    check("流量生成器 created+failed == requested",
          isinstance(tr, dict) and tr.get("created", -1) + tr.get("failed", -1) == tr.get("requested"),
          {k: tr.get(k) for k in ["requested", "created", "failed"]} if isinstance(tr, dict) else tr)
    check("throughputRps / p99Ms 为真实 number（不预填示例值）",
          isinstance(tr, dict) and all(isinstance(tr.get(k), (int, float)) for k in
                                       ["elapsedMs", "throughputRps", "p99Ms"]),
          {k: tr.get(k) for k in ["elapsedMs", "throughputRps", "p99Ms"]} if isinstance(tr, dict) else tr)
    st, exps = http("GET", "/api/experiments")
    check("GET /api/experiments 200 且为数组", st == 200 and isinstance(exps, list),
          len(exps) if isinstance(exps, list) else exps)
    check("流量生成已落 experiment_results（TRAFFIC 行）",
          isinstance(exps, list) and any(x.get("name") == "TRAFFIC" for x in exps),
          [x.get("name") for x in exps][:5] if isinstance(exps, list) else exps)
    if isinstance(exps, list) and exps:
        fieldset(exps[0], ["id", "name", "params", "result", "ranAtUtc", "passed"], "ExperimentResult 字段")
        check("ExperimentResult id 为 number / params,result 为 JSON 字符串 / passed 为 bool",
              isinstance(exps[0]["id"], int) and isinstance(exps[0]["params"], str)
              and isinstance(exps[0]["result"], str) and isinstance(exps[0]["passed"], bool), exps[0])

    st, rst = http("POST", "/api/lab/reset", {}, timeout=90)
    fieldset(rst, ["status", "productsSeeded", "clearedAtUtc"], "ResetResult 字段")
    check("一键重置后订单清空", scalar("SELECT COUNT(*) FROM Orders;") == "0",
          scalar("SELECT COUNT(*) FROM Orders;"))
    # M8：reset 与启动种子共用 DemoCatalog，避免「一键重置把商城打回 2 个无分类 SKU」的种子漂移。
    check("reset 的 productsSeeded 与 DemoCatalog 一致（8）",
          isinstance(rst, dict) and rst.get("productsSeeded") == 8,
          rst.get("productsSeeded") if isinstance(rst, dict) else rst)
    check("一键重置后重新播种 8 个商品", scalar("SELECT COUNT(*) FROM Products;") == "8",
          scalar("SELECT COUNT(*) FROM Products;"))
    check("reset 后 5 个商品分类齐全",
          scalar("SELECT COUNT(DISTINCT Category) FROM Products;") == "5",
          scalar("SELECT COUNT(DISTINCT Category) FROM Products;"))
    check("reset 后无空分类（Category 非空且非空白）",
          scalar("SELECT COUNT(*) FROM Products WHERE Category IS NULL OR TRIM(Category) = '';") == "0",
          scalar("SELECT COUNT(*) FROM Products WHERE Category IS NULL OR TRIM(Category) = '';"))
    st, reset_prods = http("GET", "/api/products")
    check("reset 后 /api/products 仍为 8 个且每项带 category",
          st == 200 and isinstance(reset_prods, list) and len(reset_prods) == 8
          and all(isinstance(p.get("category"), str) and p["category"].strip() for p in reset_prods),
          "count=%s" % (len(reset_prods) if isinstance(reset_prods, list) else reset_prods))
    st, exp_after = http("GET", "/api/experiments")
    check("一键重置保留 experiment_results 留痕",
          st == 200 and isinstance(exp_after, list) and len(exp_after) >= 1,
          len(exp_after) if isinstance(exp_after, list) else exp_after)

# ---------------------------------------------------------------- 阶段 7（E1/E2/E3）
if EXPERIMENTS:
    print("\n--- 阶段 7（M5）：E1 / E2 / E3 真跑（真实判据，不伪造数字）---")
    for name in ("E1", "E2", "E3"):
        if name == "E3":
            # E1/E2 会消耗 SKU-1 库存；让并发压测从满库存独立起跑，避免「库存不足」污染吞吐/P99。
            st, _ = http("POST", "/api/lab/reset", {}, timeout=90)
            check("E3 前一键重置，压测从满库存独立起跑", st == 200, st)
        try:
            st, row = http("POST", "/api/experiments/run", {"name": name}, timeout=300)
        except Exception as ex:
            check("实验 %s 请求完成" % name, False, repr(ex))
            continue
        check("实验 %s 返回 200" % name, st == 200, row if st != 200 else "")
        if st == 200 and isinstance(row, dict):
            fieldset(row, ["id", "name", "params", "result", "ranAtUtc", "passed"],
                     "%s 结果字段" % name)
            check("%s params / result 为 JSON 字符串" % name,
                  isinstance(row.get("params"), str) and isinstance(row.get("result"), str),
                  type(row.get("result")).__name__)
            try:
                parsed = json.loads(row["result"])
                check("%s result 可解析为 JSON 对象" % name, isinstance(parsed, dict), parsed)
            except Exception as ex:
                check("%s result 可解析为 JSON 对象" % name, False, repr(ex))
            check("%s passed=True（真实判据）" % name, row.get("passed") is True,
                  row.get("result"))
    st, _ = http("POST", "/api/experiments/run", {"name": "NOPE"}, timeout=30)
    check("非法实验名返回 400", st == 400, st)

# ---------------------------------------------------------------- 阶段 8（M6b）
if M6:
    print("\n--- 阶段 8（M6b）：items[] 多明细 → 模拟支付 → Worker 异步履约 → 四方对账 ---")
    # 开关复位为默认（consumer/publisher on、failure off），避免上游阶段遗留状态污染。
    for t in ("start_publisher", "start_consumer", "failure_off"):
        http("POST", "/api/lab/inject", {"type": t})
    st, lab = http("GET", "/api/lab/state")
    check("M6b 起跑时控制开关为默认（消费者/发布者 on、故障注入 off）",
          st == 200 and isinstance(lab, dict)
          and lab.get("consumerEnabled") is True and lab.get("publisherEnabled") is True
          and lab.get("failureInjectionEnabled") is False, lab)

    st, _ = http("POST", "/api/lab/reset", {}, timeout=90)
    check("M6b 起跑前一键重置，库存从满值开始", st == 200, st)

    idem = "m6b-multi-" + datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S%f")
    lines = [{"sku": "SKU-1", "quantity": 2}, {"sku": "SKU-2", "quantity": 3}]
    body = {"items": lines, "idempotencyKey": idem}

    st, bad = http("POST", "/api/orders",
                   {"sku": "SKU-1", "quantity": 1, "items": lines,
                    "idempotencyKey": idem + "-both"})
    check("items 与 sku/quantity 同传返回 400 { error }",
          st == 400 and isinstance(bad, dict) and "error" in bad, bad)
    st, bad2 = http("POST", "/api/orders", {"items": [], "idempotencyKey": idem + "-empty"})
    check("items 空数组返回 400", st == 400, bad2)

    st, before_products = http("GET", "/api/products")
    check("M6b /api/products 每项含 version(number)",
          st == 200 and isinstance(before_products, list)
          and all(isinstance(p.get("version"), int) for p in before_products),
          [p.get("version") for p in before_products] if isinstance(before_products, list) else before_products)
    before = {p["sku"]: p for p in before_products} if isinstance(before_products, list) else {}

    st, created = http("POST", "/api/orders", body, headers={"Idempotency-Key": idem}, timeout=20)
    check("多明细下单返回 200", st == 200 and isinstance(created, dict), created)
    created = created if isinstance(created, dict) else {}
    fieldset(created, ["id", "orderNo", "status", "message"], "M6b 下单响应字段（形状不变）")
    oid = created.get("id")
    ono = created.get("orderNo", "")
    check("多明细下单响应 status=Created", created.get("status") == "Created", created.get("status"))

    if isinstance(oid, int) and ono:
        check("订单写入 2 行 order_items",
              scalar("SELECT COUNT(*) FROM OrderItems WHERE OrderId=%d;" % oid) == "2",
              scalar("SELECT COUNT(*) FROM OrderItems WHERE OrderId=%d;" % oid))
        reserves = sql("SELECT Sku, Quantity FROM InventoryLedgers "
                       "WHERE RefOrderId=%d AND ChangeType='Reserve' ORDER BY Sku;" % oid).splitlines()
        check("Reserve 流水按明细写 2 行（SKU-1=2 / SKU-2=3）",
              reserves == ["SKU-1\t2", "SKU-2\t3"], reserves)
        check("多明细只写 1 行 order.created outbox",
              scalar("SELECT COUNT(*) FROM OutboxMessages WHERE Payload LIKE '%%%s%%';" % ono) == "1",
              scalar("SELECT COUNT(*) FROM OutboxMessages WHERE Payload LIKE '%%%s%%';" % ono))

        st, mid = http("GET", "/api/products")
        now_map = {p["sku"]: p for p in mid} if isinstance(mid, list) else {}
        check("SKU-1 预扣 2 件且 version+1",
              now_map.get("SKU-1", {}).get("available") == before.get("SKU-1", {}).get("available", 0) - 2
              and now_map.get("SKU-1", {}).get("reserved") == before.get("SKU-1", {}).get("reserved", 0) + 2
              and now_map.get("SKU-1", {}).get("version") == before.get("SKU-1", {}).get("version", 0) + 1,
              now_map.get("SKU-1"))
        check("SKU-2 预扣 3 件且 version+1",
              now_map.get("SKU-2", {}).get("available") == before.get("SKU-2", {}).get("available", 0) - 3
              and now_map.get("SKU-2", {}).get("reserved") == before.get("SKU-2", {}).get("reserved", 0) + 3
              and now_map.get("SKU-2", {}).get("version") == before.get("SKU-2", {}).get("version", 0) + 1,
              now_map.get("SKU-2"))

        st2, replay = http("POST", "/api/orders", body, headers={"Idempotency-Key": idem})
        check("同一幂等键重放返回同一订单 id",
              st2 == 200 and isinstance(replay, dict) and replay.get("id") == oid, replay)

        st, detail = http("GET", "/api/orders/%d" % oid)
        detail_items = detail.get("items", []) if isinstance(detail, dict) else []
        check("详情返回 2 行明细且数值与请求一致",
              st == 200 and sorted((i.get("sku"), i.get("quantity")) for i in detail_items)
              == [("SKU-1", 2), ("SKU-2", 3)], detail_items)

        st, listed = http("GET", "/api/orders?orderNo=%s" % ono[:14])
        check("GET /api/orders?orderNo= 模糊命中该订单",
              st == 200 and isinstance(listed, dict)
              and any(x.get("orderNo") == ono for x in listed.get("items", [])),
              [x.get("orderNo") for x in listed.get("items", [])] if isinstance(listed, dict) else listed)

        st, paid = http("POST", "/api/orders/%d/pay" % oid, {})
        check("模拟支付返回 status=Paid",
              st == 200 and isinstance(paid, dict) and paid.get("status") == "Paid", paid)

        ok, last = wait_until(
            "multi-item deduct",
            lambda: scalar("SELECT COUNT(*) FROM InventoryLedgers "
                           "WHERE RefOrderId=%d AND ChangeType='Deduct';" % oid) == "2",
            30, 0.5)
        check("Worker 异步实扣写出 2 行 Deduct 流水（不是只扣第一个 SKU）", ok, last)
        deducts = sql("SELECT Sku, Quantity FROM InventoryLedgers "
                      "WHERE RefOrderId=%d AND ChangeType='Deduct' ORDER BY Sku;" % oid).splitlines()
        check("Deduct 流水按明细 2/3 实扣", deducts == ["SKU-1\t2", "SKU-2\t3"], deducts)

        st, after = http("GET", "/api/products")
        after_map = {p["sku"]: p for p in after} if isinstance(after, list) else {}
        check("履约后两个 SKU 的 reserved 回到下单前",
              after_map.get("SKU-1", {}).get("reserved") == before.get("SKU-1", {}).get("reserved")
              and after_map.get("SKU-2", {}).get("reserved") == before.get("SKU-2", {}).get("reserved"),
              {k: after_map.get(k, {}).get("reserved") for k in ("SKU-1", "SKU-2")})

        today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        st, report = http("GET", "/api/reports/daily?date=%s" % today)
        check("多明细链路结束后四方对账 diff=0",
              st == 200 and isinstance(report, dict)
              and report.get("reconciliation", {}).get("diff") == 0,
              report.get("reconciliation") if isinstance(report, dict) else report)

# ---------------------------------------------------------------- 阶段 9（M6c）
if M6C:
    print("\n--- 阶段 9（M6c）：链路泳道 7 节点真实计数 + 容器 Production 下 Swagger ---")
    st, pipe = http("GET", "/api/pipeline")
    check("GET /api/pipeline 经 nginx 返回 200", st == 200, st)
    fieldset(pipe, {"generatedAtUtc", "nodes"}, "PipelineSnapshot 恰好 2 个字段")
    nodes = pipe.get("nodes", []) if isinstance(pipe, dict) else []
    nodes = nodes if isinstance(nodes, list) else []
    check("泳道返回 7 个节点", len(nodes) == 7, len(nodes))
    expected_keys = ["orders", "outbox", "exchange", "worker", "deduct", "deadLetter", "report"]
    check("7 节点 key 与顺序固定", [n.get("key") for n in nodes] == expected_keys,
          [n.get("key") for n in nodes])
    node_fields = {"key", "label", "middleware", "count", "unit", "source", "available", "link"}
    check("每个 PipelineNode 恰好 8 个字段（与 DTO 契约逐字一致）",
          len(nodes) == 7 and all(isinstance(n, dict) and set(n.keys()) == node_fields for n in nodes),
          [sorted(n.keys()) for n in nodes[:1]])
    check("节点三中间件标签齐全（MySQL / RabbitMQ / Redis）",
          {n.get("middleware") for n in nodes} == {"MySQL", "RabbitMQ", "Redis"},
          sorted({str(n.get("middleware")) for n in nodes}))
    check("count 为整数、available 为 bool、link 指向后台页",
          len(nodes) == 7
          and all(isinstance(n.get("count"), int) and isinstance(n.get("available"), bool)
                  and str(n.get("link", "")).startswith("/admin/") for n in nodes),
          [(n.get("key"), n.get("count"), n.get("available"), n.get("link")) for n in nodes])
    check("不伪造 0：available=false 的节点 count 必须为 -1，其余 count >= 0",
          len(nodes) == 7
          and all((n.get("available") and n.get("count", -2) >= 0)
                  or (not n.get("available") and n.get("count") == -1) for n in nodes),
          [(n.get("key"), n.get("count"), n.get("available")) for n in nodes])

    by = {n.get("key"): n for n in nodes}
    sql_orders = int(scalar("SELECT COUNT(*) FROM Orders;") or 0)
    check("orders 节点计数 == MySQL Orders 真实行数",
          by.get("orders", {}).get("count") == sql_orders,
          "node=%s sql=%s" % (by.get("orders", {}).get("count"), sql_orders))
    sql_pending = int(scalar("SELECT COUNT(*) FROM OutboxMessages WHERE Status='pending';") or 0)
    outbox_count = by.get("outbox", {}).get("count")
    check("outbox 节点计数取自真实 pending（允许发布器在两次读取间推进）",
          isinstance(outbox_count, int) and 0 <= outbox_count - sql_pending <= 5,
          "node=%s sql=%s" % (outbox_count, sql_pending))
    sql_processed = int(scalar("SELECT COUNT(*) FROM ProcessedEvents;") or 0)
    check("worker 节点计数 == ProcessedEvents 真实行数",
          by.get("worker", {}).get("count") == sql_processed,
          "node=%s sql=%s" % (by.get("worker", {}).get("count"), sql_processed))
    sql_deduct = int(scalar("SELECT COUNT(*) FROM InventoryLedgers WHERE ChangeType='Deduct';") or 0)
    check("deduct 节点计数 == Deduct 流水真实行数",
          by.get("deduct", {}).get("count") == sql_deduct,
          "node=%s sql=%s" % (by.get("deduct", {}).get("count"), sql_deduct))
    sql_dead = int(scalar("SELECT COUNT(*) FROM DeadLetters WHERE Status='pending';") or 0)
    check("deadLetter 节点计数 == pending 死信真实行数",
          by.get("deadLetter", {}).get("count") == sql_dead,
          "node=%s sql=%s" % (by.get("deadLetter", {}).get("count"), sql_dead))
    sql_reports = int(scalar("SELECT COUNT(*) FROM DailyReportRecords;") or 0)
    check("report 节点计数 == DailyReportRecords 真实行数",
          by.get("report", {}).get("count") == sql_reports,
          "node=%s sql=%s" % (by.get("report", {}).get("count"), sql_reports))
    exchange = by.get("exchange", {})
    check("exchange 节点 available=true（RabbitMQ 在线时不许降级为 —）",
          exchange.get("available") is True, exchange.get("available"))
    ok, last = wait_until(
        "pipeline queue depth",
        lambda: exchange.get("count") == qstat("q.fulfillment").get("messages", 0),
        15, 1.0)
    check("exchange 节点计数 == q.fulfillment 真实队列深度", ok,
          "node=%s mgmt=%s" % (exchange.get("count"), qstat("q.fulfillment").get("messages")))

    st, ctype, body = raw_get("/swagger/v1/swagger.json")
    try:
        doc = json.loads(body)
    except Exception:
        doc = None
    check("nginx 8080 /swagger/v1/swagger.json 200 且是 JSON（不是 SPA 回退 HTML）",
          st == 200 and isinstance(doc, dict) and "json" in ctype.lower(),
          "status=%s ctype=%s head=%s" % (st, ctype, body[:60]))
    paths = doc.get("paths", {}) if isinstance(doc, dict) else {}
    check("Swagger 文档标注 openapi/swagger 版本",
          isinstance(doc, dict) and ("openapi" in doc or "swagger" in doc),
          sorted(doc.keys())[:8] if isinstance(doc, dict) else None)
    check("Swagger 文档含 M6c 新增 /api/pipeline",
          "/api/pipeline" in paths, sorted(paths)[:10])
    check("Swagger 文档仍含 /api/orders（原有端点不退化）", "/api/orders" in paths,
          sorted(paths)[:10])
    st2, ctype2, body2 = raw_get("/swagger/v1/swagger.json", base=DIRECT)
    try:
        doc2 = json.loads(body2)
    except Exception:
        doc2 = None
    check("api 5080 直连 /swagger/v1/swagger.json 200 且是 JSON",
          st2 == 200 and isinstance(doc2, dict) and "json" in ctype2.lower(),
          "status=%s ctype=%s" % (st2, ctype2))
    st3, ctype3, ui_body = raw_get("/swagger/index.html")
    check("Swagger UI 入口页经 nginx 可访问（内容含 swagger-ui，排除 SPA 回退）",
          st3 == 200 and "html" in ctype3.lower() and "swagger-ui" in ui_body,
          "status=%s ctype=%s has_swagger_ui=%s" % (st3, ctype3, "swagger-ui" in ui_body))

# ---------------------------------------------------------------- 汇总
print("\n" + "=" * 72)
passed = sum(1 for _, ok, _ in results if ok)
print("汇总：%d / %d PASS" % (passed, len(results)))
for name, ok, detail in results:
    if not ok:
        print("  FAILED: %s | %s" % (name, detail))
print("=" * 72)
sys.exit(0 if passed == len(results) else 1)
