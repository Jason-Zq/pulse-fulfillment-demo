#!/usr/bin/env python3
"""M10 10.2：真验证 RabbitMQ 队列 TTL 15 分钟【自然到期】关单链路。

与 scripts/verify_docker_e2e.py --deep 的本质区别：
  --deep 把 expiresAtUtc 改成 2020 年，再用管理台直投 order.timeout 探针消息，
  属于「模拟到期」，只验证消费端逻辑；本脚本不改时间 / 不改 DB / 不手工投递，
  让 order.created 在 q.order.timeout 里真等满 900000 ms，由 RabbitMQ 自己
  DLX 到 order.timeout，Worker 消费后由 TimeoutOrderService 关单。

后端还有一道不可伪造的校验：TimeoutOrderService 只在
  order.ExpiresAtUtc <= DateTime.UtcNow 时才关单，
所以提前触发（伪造消息）不会让订单变成 Cancelled。

严格约束：不 mock 时间、不缩短 TTL、不手工投递消息、不改 ExpiresAtUtc。
前置：docker compose up -d（六服务 healthy），q.order.timeout 当前无积压。
用法：python3 scripts/verify_ttl_expiry.py [--buffer 60]
输出：docs/evidence/m10-ttl-expiry.json（真实时间戳 / 实测值 / 逐条断言）
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
BASE = "http://localhost:8080"      # nginx -> api，模拟真实用户路径
MGMT = "http://localhost:15672/api"
AUTH = base64.b64encode(b"guest:guest").decode()
OUT = os.path.join(CWD, "docs", "evidence", "m10-ttl-expiry.json")

BUFFER = 60
if "--buffer" in sys.argv:
    BUFFER = int(sys.argv[sys.argv.index("--buffer") + 1])

SKU = "SKU-1"
QUANTITY = 2

results = []
evidence = {
    "subject": "RabbitMQ TTL 15 分钟自然到期 -> DLX -> 超时关单",
    "constraints": [
        "不 mock 时间（只用真实墙钟 time.time / datetime.now(UTC)）",
        "不缩短 x-message-ttl（脚本只读拓扑参数，不改）",
        "不手工投递 order.timeout 探针消息（脚本无任何 publish 调用）",
        "不改数据库里的 ExpiresAtUtc / CreatedAtUtc",
    ],
    "sku": SKU,
    "quantity": QUANTITY,
    "bufferSeconds": BUFFER,
    "samples": [],
    "checks": [],
}


def check(name, cond, detail=""):
    ok = bool(cond)
    results.append((name, ok, str(detail)))
    evidence["checks"].append({"name": name, "ok": ok, "detail": str(detail)})
    print(("PASS  " if ok else "FAIL  ") + name + (("  | " + str(detail)) if detail else ""), flush=True)
    return ok


def http(method, path, body=None, base=BASE, timeout=10):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
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


def mgmt(path):
    req = urllib.request.Request(MGMT + path)
    req.add_header("Authorization", "Basic " + AUTH)
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.loads(r.read().decode() or "null")


def qstat(name):
    return mgmt("/queues/%2F/" + name)


def sql(query):
    p = subprocess.run(
        ["docker", "compose", "exec", "-T", "mysql", "mysql",
         "-uroot", "-ppulse_dev", "pulse", "-N", "-B", "-e", query],
        cwd=CWD, capture_output=True, text=True)
    if p.returncode != 0:
        return "SQLERR:" + p.stderr.strip()
    return p.stdout.strip()


def scalar(query):
    out = sql(query)
    return out.splitlines()[0] if out.splitlines() else ""


def row(query):
    out = scalar(query)
    return out.split("\t") if out else []


def parse_utc(s):
    """MySQL DATETIME / System.Text.Json 的 DateTime 均按 UTC 解释。"""
    s = (s or "").strip()
    if not s:
        return None
    if s.endswith("Z"):
        return datetime.fromisoformat(s.replace("Z", "+00:00")).timestamp()
    s = s.replace(" ", "T")
    dt = datetime.fromisoformat(s)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.timestamp()


def iso(ts=None):
    return datetime.fromtimestamp(ts if ts is not None else time.time(), timezone.utc).isoformat()


def wait_until(fn, timeout=60, interval=2):
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


def save():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    evidence["finishedAtUtc"] = iso()
    evidence["pass"] = sum(1 for _, ok, _ in results if ok)
    evidence["fail"] = sum(1 for _, ok, _ in results if not ok)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(evidence, f, ensure_ascii=False, indent=2)
    print("EVIDENCE=" + OUT, flush=True)


def main():
    started = time.time()
    evidence["startedAtUtc"] = iso(started)
    print("M10 TTL 自然到期真验证启动 | " + iso(started)
          + " | 预计真等 >= 900 秒 + %d 秒缓冲" % BUFFER, flush=True)

    # ---------------------------------------------------------- 阶段 0 前置
    print("\n--- 阶段 0：前置（拓扑参数 / 队列干净 / Worker 在线）---", flush=True)
    st, health = http("GET", "/api/health")
    check("API /api/health 200", st == 200, st)

    tq = qstat("q.order.timeout")
    args = tq.get("arguments", {})
    ttl = args.get("x-message-ttl")
    dlrk = args.get("x-dead-letter-routing-key")
    check("q.order.timeout 仍是 900000 ms TTL（未为验证而缩短）", ttl == 900000, ttl)
    check("q.order.timeout DLX routing key = order.timeout", dlrk == "order.timeout", dlrk)
    check("q.order.timeout 无消费者（消息才会自然到期）", tq.get("consumers", 0) == 0,
          tq.get("consumers"))
    check("q.order.timeout 当前无积压（等待窗口干净，不擅自 purge）",
          tq.get("messages", 0) == 0, tq.get("messages"))
    ok, last = wait_until(lambda: qstat("q.fulfillment").get("consumers", 0) >= 1, 30, 1.0)
    check("q.fulfillment 有 Worker 消费者在线（到期后才会被消费）", ok, last)
    if not all(ok for _, ok, _ in results):
        print("前置不满足，终止（不造单，避免污染）", flush=True)
        save()
        return 1

    st, rst = http("POST", "/api/lab/reset", {})
    check("POST /api/lab/reset 200 且重新播种 8 SKU",
          st == 200 and isinstance(rst, dict) and rst.get("productsSeeded") == 8, rst)
    check("reset 后订单表为空", scalar("SELECT COUNT(*) FROM Orders;") == "0")
    check("reset 后死信表为空", scalar("SELECT COUNT(*) FROM DeadLetters;") == "0")

    inv_before = row("SELECT Available, Reserved, Version FROM Inventories WHERE Sku='%s';" % SKU)
    check("下单前库存快照可读", len(inv_before) == 3, inv_before)
    avail_before, reserved_before, version_before = (int(x) for x in inv_before)
    evidence["inventoryBefore"] = {"available": avail_before, "reserved": reserved_before,
                                   "version": version_before}

    # ---------------------------------------------------------- 阶段 1 造单
    print("\n--- 阶段 1：造单（不支付，让 TTL 自然到期）---", flush=True)
    key = "m10-ttl-%d" % int(started)
    st, created = http("POST", "/api/orders",
                       {"sku": SKU, "quantity": QUANTITY, "idempotencyKey": key})
    if st != 200 or not isinstance(created, dict):
        check("POST /api/orders 200", False, (st, created))
        save()
        return 1
    oid = created["id"]
    order_no = created["orderNo"]
    evidence.update({"orderId": oid, "orderNo": order_no, "idempotencyKey": key,
                     "placedResponse": created})
    check("POST /api/orders 200", True, created)
    check("下单后状态 Created", created.get("status") == "Created", created.get("status"))

    st, detail = http("GET", "/api/orders/%s" % oid)
    check("GET /api/orders/{id} 200", st == 200, st)
    api_expires = detail.get("expiresAtUtc")
    api_created = detail.get("createdAtUtc")
    evidence["apiExpiresAtUtc"] = api_expires
    evidence["apiCreatedAtUtc"] = api_created

    dbtimes = row("SELECT CreatedAtUtc, ExpiresAtUtc FROM Orders WHERE Id=%s;" % oid)
    check("MySQL 可读下单/截止时间", len(dbtimes) == 2, dbtimes)
    created_ts = parse_utc(dbtimes[0])
    expires_ts = parse_utc(dbtimes[1])
    evidence["dbCreatedAtUtc"] = dbtimes[0]
    evidence["dbExpiresAtUtc"] = dbtimes[1]
    evidence["expiresAtEpoch"] = expires_ts
    check("订单 TTL 契约：ExpiresAtUtc - CreatedAtUtc == 900 秒",
          abs((expires_ts - created_ts) - 900) <= 1,
          "delta=%.3fs" % (expires_ts - created_ts))
    check("下单预扣 available-%d / reserved+%d" % (QUANTITY, QUANTITY),
          int(scalar("SELECT Available FROM Inventories WHERE Sku='%s';" % SKU)) == avail_before - QUANTITY
          and int(scalar("SELECT Reserved FROM Inventories WHERE Sku='%s';" % SKU)) == reserved_before + QUANTITY)
    check("写入 1 条 Reserve 流水",
          scalar("SELECT COUNT(*) FROM InventoryLedgers WHERE RefOrderId=%s AND ChangeType='Reserve';" % oid) == "1")
    ok, last = wait_until(
        lambda: scalar("SELECT COUNT(*) FROM OutboxMessages WHERE RoutingKey='order.created' AND Status='published';") == "1",
        30, 1.0)
    check("outbox order.created 已投递", ok, last)
    ok, last = wait_until(lambda: qstat("q.order.timeout").get("messages", 0) >= 1, 30, 1.0)
    check("q.order.timeout 积压 1 条（开始真等 TTL）", ok, last)

    # ---------------------------------------------------------- 阶段 2 真等
    print("\n--- 阶段 2：按真实墙钟等待 TTL 自然到期（不 mock 时间）---", flush=True)
    deadline = expires_ts + BUFFER
    sample_interval = 30
    while True:
        now = time.time()
        if now >= deadline:
            break
        try:
            s, d = http("GET", "/api/orders/%s" % oid)
            q = qstat("q.order.timeout")
            sample = {
                "atUtc": iso(now),
                "secondsToExpiry": round(expires_ts - now, 1),
                "status": d.get("status") if isinstance(d, dict) else None,
                "timeoutQueueMessages": q.get("messages"),
            }
        except Exception as ex:
            sample = {"atUtc": iso(now), "error": repr(ex)}
        evidence["samples"].append(sample)
        print("  等待中 | 距到期 %.1fs | status=%s | q.order.timeout=%s"
              % (expires_ts - now, sample.get("status"), sample.get("timeoutQueueMessages")), flush=True)
        time.sleep(min(sample_interval, max(1.0, deadline - time.time())))

    waited = time.time() - started
    evidence["waitedSeconds"] = round(waited, 1)
    evidence["expiryReachedAtUtc"] = iso(time.time())
    check("真实墙钟等待 >= 900 秒（时间没有被 mock / 跳过）", waited >= 900,
          "waited=%.1fs" % waited)

    early = [s for s in evidence["samples"]
             if s.get("secondsToExpiry", -1) > 0 and s.get("status") is not None
             and s.get("status") != "Created"]
    check("到期前订单一直是 Created（没有被任何探针/人工路径提前关单）",
          len(early) == 0 and len(evidence["samples"]) >= 10,
          "samples=%d, earlyCancelled=%d" % (len(evidence["samples"]), len(early)))
    held = [s for s in evidence["samples"]
            if s.get("secondsToExpiry", -1) > 0 and s.get("timeoutQueueMessages") is not None
            and s.get("timeoutQueueMessages") >= 1]
    check("到期前消息一直留在 q.order.timeout（TTL 未被绕过）",
          len(held) >= 5, "heldSamples=%d" % len(held))

    # ---------------------------------------------------------- 阶段 3 到期后
    print("\n--- 阶段 3：自然到期后核对关单 / 释放 / 队列排空 ---", flush=True)
    ok, last = wait_until(
        lambda: http("GET", "/api/orders/%s" % oid)[1].get("status") == "Cancelled",
        180, 2.0)
    check("TTL 自然到期后订单 Created -> Cancelled（Worker 真消费 DLX）", ok, last)
    cancelled_at = time.time()
    evidence["cancelledAtUtc"] = iso(cancelled_at)
    evidence["secondsFromExpiryToCancelled"] = round(cancelled_at - expires_ts, 1)

    st, final = http("GET", "/api/orders/%s" % oid)
    final_status = final.get("status") if isinstance(final, dict) else None
    evidence["finalOrderStatus"] = final_status
    check("订单详情状态为 Cancelled（API 语义值，不拿裸整数列比对）",
          final_status == "Cancelled", final_status)

    ok, last = wait_until(lambda: qstat("q.order.timeout").get("messages", 0) == 0, 60, 2.0)
    check("q.order.timeout 已排空（消息确实到期离开队列）", ok, last)

    rel = row("SELECT COUNT(*), COALESCE(SUM(Quantity),0) FROM InventoryLedgers "
              "WHERE RefOrderId=%s AND ChangeType='Release';" % oid)
    check("按该订单落 1 条 Release 流水且数量 = %d" % QUANTITY,
          len(rel) == 2 and rel[0] == "1" and int(float(rel[1])) == QUANTITY, rel)

    inv_after = row("SELECT Available, Reserved, Version FROM Inventories WHERE Sku='%s';" % SKU)
    evidence["inventoryAfter"] = {"available": inv_after[0] if inv_after else None,
                                  "reserved": inv_after[1] if inv_after else None,
                                  "version": inv_after[2] if inv_after else None}
    check("预扣库存完整恢复（available=%d / reserved=%d）" % (avail_before, reserved_before),
          len(inv_after) == 3 and int(inv_after[0]) == avail_before
          and int(inv_after[1]) == reserved_before, inv_after)
    check("库存 Version 因 Reserve + Release 各 +1（共 +2）",
          len(inv_after) == 3 and int(inv_after[2]) == version_before + 2,
          "before=%d after=%s" % (version_before, inv_after[2] if inv_after else "?"))
    check("超时关单未产生死信", scalar("SELECT COUNT(*) FROM DeadLetters;") == "0",
          scalar("SELECT COUNT(*) FROM DeadLetters;"))
    check("订单未被误标为已支付（PaidAtUtc 仍为 NULL，全程未调用 /pay）",
          scalar("SELECT COUNT(*) FROM Orders WHERE Id=%s AND PaidAtUtc IS NULL;" % oid) == "1",
          scalar("SELECT COUNT(*) FROM Orders WHERE Id=%s AND PaidAtUtc IS NULL;" % oid))

    save()
    p = sum(1 for _, ok, _ in results if ok)
    f = sum(1 for _, ok, _ in results if not ok)
    print("\n========== M10 TTL 自然到期真验证汇总 ==========", flush=True)
    print("PASS=%d  FAIL=%d  waited=%.1fs  exit=%d" % (p, f, waited, 1 if f else 0), flush=True)
    print("TTL_EXIT=%d" % (1 if f else 0), flush=True)
    return 1 if f else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        evidence["fatal"] = repr(exc)
        save()
        print("FATAL " + repr(exc), flush=True)
        print("TTL_EXIT=1", flush=True)
        sys.exit(1)
