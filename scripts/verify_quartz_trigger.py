#!/usr/bin/env python3
"""M10 10.5：真验证 Quartz 调度器【自己到点触发】日报写入。

做法：把 Reports__DailyCron 用 shell 环境变量临时设为短 cron，重建 worker
（镜像含 M10 的 DailyReportCron 接线），确保昨日 UTC 的 DailyReportRecords 行
不存在，然后**不调用任何手工日报端点、不直接 INSERT、不 mock 时间**，
等调度器自己把该行写回来，并核对 Worker 日志里的 cron 与「日报持久化完成」。
验证完不带该环境变量再重建，恢复默认 01:00（Asia/Shanghai）。

前置：docker compose up -d 六服务 healthy；本机已能 build Worker 镜像。
运行：python3 scripts/verify_quartz_trigger.py
输出：docs/evidence/m10-quartz-trigger.json
注意：会重建 worker 容器，务必在 15 分钟 TTL 真等待【结束后】再运行。
"""
import json
import os
import subprocess
import sys
import time
from datetime import datetime, timedelta, timezone

CWD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(CWD, "docs", "evidence", "m10-quartz-trigger.json")
SHORT_CRON = "0/10 * * * * ?"
DEFAULT_CRON = "0 0 1 * * ?"
STAT_DATE = (datetime.now(timezone.utc) - timedelta(days=1)).date().isoformat()

results = []
evidence = {
    "subject": "Quartz 调度器到点自动触发 DailyReportJob 写入昨日 UTC 日报",
    "constraints": [
        "不调用任何手工日报写入端点（POST /api/reports/daily 等）",
        "不直接 INSERT / UPDATE 日报行（只 DELETE 制造可观测窗口）",
        "不 mock 时间；短 cron 让真实调度器按真实墙钟触发",
        "验证完恢复默认 0 0 1 * * ?，不留短 cron",
    ],
    "statDate": STAT_DATE,
    "shortCron": SHORT_CRON,
    "defaultCron": DEFAULT_CRON,
    "checks": [],
}


def check(name, cond, detail=""):
    ok = bool(cond)
    results.append((name, ok, str(detail)))
    evidence["checks"].append({"name": name, "ok": ok, "detail": str(detail)})
    print(("PASS  " if ok else "FAIL  ") + name + (("  | " + str(detail)) if detail else ""), flush=True)
    return ok


def compose(*args, env=None):
    return subprocess.run(
        ["docker", "compose", *args], cwd=CWD, capture_output=True, text=True, env=env)


def sql(query):
    p = subprocess.run(
        ["docker", "compose", "exec", "-T", "mysql", "mysql",
         "-uroot", "-ppulse_dev", "pulse", "-N", "-B", "-e", query],
        cwd=CWD, capture_output=True, text=True)
    if p.returncode != 0:
        return "SQLERR:" + p.stderr.strip()
    return p.stdout.strip()


def scalar(query):
    lines = sql(query).splitlines()
    return lines[0] if lines else ""


def worker_logs():
    p = compose("logs", "--no-color", "--tail", "400", "worker")
    return (p.stdout or "") + (p.stderr or "")


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


def row_for(stat_date):
    return scalar(
        "SELECT CONCAT(StatDate,'|',OrderCnt,'|',PaidCnt,'|',CancelCnt) "
        "FROM DailyReportRecords WHERE StatDate='%s';" % stat_date)


def save():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    evidence["finishedAtUtc"] = datetime.now(timezone.utc).isoformat()
    evidence["pass"] = sum(1 for _, ok, _ in results if ok)
    evidence["fail"] = sum(1 for _, ok, _ in results if not ok)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(evidence, f, ensure_ascii=False, indent=2)
    print("EVIDENCE=" + OUT, flush=True)


def main():
    started = time.time()
    evidence["startedAtUtc"] = datetime.now(timezone.utc).isoformat()
    print("M10 Quartz 自动触发真验证启动 | statDate=%s | shortCron=%s"
          % (STAT_DATE, SHORT_CRON), flush=True)

    # 外部 shell 若残留 DAILY_REPORT_CRON，会污染「默认口径」判定，显式剔除。
    clean_env = {k: v for k, v in os.environ.items() if k != "DAILY_REPORT_CRON"}

    # ------------------------------------------------------------ 阶段 0 默认口径
    print("\n--- 阶段 0：默认口径与插值 ---", flush=True)
    default_cfg = compose("config", env=clean_env).stdout or ""
    check("未设环境变量时 compose 默认 cron 仍是 01:00",
          DEFAULT_CRON in default_cfg, [l.strip() for l in default_cfg.splitlines()
                                        if "Reports__DailyCron" in l])
    short_env = {**os.environ, "DAILY_REPORT_CRON": SHORT_CRON}
    short_cfg = compose("config", env=short_env).stdout or ""
    check("设 DAILY_REPORT_CRON 后 compose 插值成短 cron",
          SHORT_CRON in short_cfg, [l.strip() for l in short_cfg.splitlines()
                                    if "Reports__DailyCron" in l])

    # ------------------------------------------------------------ 阶段 1 构建新镜像
    print("\n--- 阶段 1：构建含 M10 接线的 Worker 镜像 ---", flush=True)
    build = compose("build", "worker")
    check("docker compose build worker 成功", build.returncode == 0,
          (build.stdout + build.stderr)[-600:] if build.returncode != 0 else "ok")
    if build.returncode != 0:
        save()
        return 1

    # ------------------------------------------------------------ 阶段 2 短 cron 真触发
    short_ok = False
    try:
        print("\n--- 阶段 2：短 cron 重建 worker，等调度器自己写日报 ---", flush=True)
        up = compose("up", "-d", "--force-recreate", "--no-deps", "worker", env=short_env)
        check("短 cron 重建 worker 成功", up.returncode == 0,
              (up.stdout + up.stderr)[-400:] if up.returncode != 0 else "ok")
        if up.returncode != 0:
            return 1

        ok, last = wait_until(
            lambda: ("Quartz 日报调度已启动" in worker_logs()
                     and SHORT_CRON in worker_logs()),
            60, 2.0)
        check("Worker 日志确认调度器以短 cron 启动（不是默认 01:00）", ok, last)

        # 可观测窗口必须放在调度器【已在运行】之后再删行：若在重建 Worker 之前删，
        # 等日志期间 Quartz 早就触发写过了，计时会得到误导性的 0.1s。
        # 现在测到的是「删行后等到下一个 cron 周期自动写回」的真实耗时。
        existed = row_for(STAT_DATE)
        evidence["rowExistedBefore"] = existed or None
        if existed:
            sql("DELETE FROM DailyReportRecords WHERE StatDate='%s';" % STAT_DATE)
        window_empty = row_for(STAT_DATE) == ""
        if not window_empty:
            # 极小概率：调度器恰好在 DELETE 与查询之间触发，再清一次窗口。
            sql("DELETE FROM DailyReportRecords WHERE StatDate='%s';" % STAT_DATE)
            window_empty = row_for(STAT_DATE) == ""
        check("调度器运行中删除昨日日报行，制造可观测窗口", window_empty,
              "before=%r after=%r" % (existed, row_for(STAT_DATE)))

        t0 = time.time()
        ok, last = wait_until(lambda: row_for(STAT_DATE) != "", 40, 1.0)
        elapsed = time.time() - t0
        evidence["secondsToAutoPersist"] = round(elapsed, 1)
        evidence["rowAfterTrigger"] = row_for(STAT_DATE) or None
        check("未调用任何手工端点，删行后由调度器周期性自动写回昨日 UTC 日报", ok,
              "elapsed=%.1fs row=%s" % (elapsed, row_for(STAT_DATE)))
        short_ok = ok

        logs = worker_logs()
        # DateOnly 的日志文本按容器 culture 输出（实测 11/07/2026），不能写死 ISO 格式；
        # 这里解析日志里的真实日期，再与「昨日 UTC」比对，避免把 culture 差异误判为未触发。
        persist_lines = [l.strip() for l in logs.splitlines()
                         if "日报持久化完成: statDate=" in l]

        def logged_stat_date(line):
            raw = line.split("statDate=", 1)[1].strip()
            for fmt in ("%Y-%m-%d", "%m/%d/%Y"):
                try:
                    return datetime.strptime(raw, fmt).date().isoformat()
                except ValueError:
                    continue
            return None

        logged_dates = [logged_stat_date(l) for l in persist_lines]
        check("Worker 日志含 DailyReportJob「日报持久化完成」且 statDate = 昨日 UTC",
              bool(persist_lines) and STAT_DATE in logged_dates,
              "lines=%s parsed=%s expected=%s"
              % (persist_lines[-2:], logged_dates[-2:], STAT_DATE))
        evidence["shortCronLogSnippets"] = [
            l.strip() for l in logs.splitlines()
            if "Quartz 日报调度已启动" in l or "日报持久化完成" in l][-6:]
        evidence["persistLogDateFormat"] = persist_lines[-1] if persist_lines else None
        check("删行后在一个短 cron 周期内自动写回（<= 40 秒，非等到 01:00）",
              short_ok and elapsed <= 40, "elapsed=%.1fs" % elapsed)
    finally:
        # -------------------------------------------------------- 阶段 3 恢复默认
        print("\n--- 阶段 3：恢复默认 01:00（无论验证成败都执行）---", flush=True)
        restore = compose("up", "-d", "--force-recreate", "--no-deps", "worker", env=clean_env)
        check("恢复默认配置重建 worker 成功", restore.returncode == 0,
              (restore.stdout + restore.stderr)[-400:] if restore.returncode != 0 else "ok")
        ok, last = wait_until(
            lambda: ("Quartz 日报调度已启动" in worker_logs()
                     and ("cron=0 0 1 * * ?" in worker_logs())),
            60, 2.0)
        check("Worker 日志确认已恢复默认 cron=0 0 1 * * ?", ok, last)
        cfg = compose("config", env=clean_env).stdout or ""
        check("compose 默认口径仍为 01:00（受版本控制的文件未改）",
              DEFAULT_CRON in cfg and SHORT_CRON not in cfg,
              [l.strip() for l in cfg.splitlines() if "Reports__DailyCron" in l])
        evidence["restoredDefaultCron"] = DEFAULT_CRON

    save()
    p = sum(1 for _, ok, _ in results if ok)
    f = sum(1 for _, ok, _ in results if not ok)
    print("\n========== M10 Quartz 自动触发真验证汇总 ==========", flush=True)
    print("PASS=%d  FAIL=%d  exit=%d" % (p, f, 1 if f else 0), flush=True)
    print("QUARTZ_EXIT=%d" % (1 if f else 0), flush=True)
    return 1 if f else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        evidence["fatal"] = repr(exc)
        save()
        print("FATAL " + repr(exc), flush=True)
        print("QUARTZ_EXIT=1", flush=True)
        sys.exit(1)
