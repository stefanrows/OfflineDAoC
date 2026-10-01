#!/usr/bin/env python3
"""Battlegroup load check (docs/TASKS.md item 45), read-only.

Joins three per-minute server log lines and compares minutes with companion
groups in a battlegroup against the minutes without:
  BATTLEGROUP_LOAD   owners / companion groups / companions in RvR
  SERVER_WORK        game-loop tick p95 and over-budget stage ticks
  BOT_THINK_PROFILE  NavPathQuery (pathing) milliseconds and calls

Usage: battlegroup-load.py [server-console.log] [--min-groups N] [--tail MB]
Default log: $OFFLINE_DAOC_ROOT/runtime/logs/server-console.log
(OFFLINE_DAOC_ROOT defaults to /mnt/d/Games/OfflineDAoC). Only the last --tail
megabytes (default 64) are read. Log lines carry HH:MM:SS only, so a day rollover
is detected when the clock goes backwards.
"""
import argparse
import os
import re
import statistics
import sys

STAMP = re.compile(r"^(\d\d):(\d\d):(\d\d) \| ")
LOAD = re.compile(r"BATTLEGROUP_LOAD battlegroups=(\d+) owners=(\d+) ownersInRvr=(\d+) "
                  r"companionGroups=(\d+) companions=(\d+) companionsInRvr=(\d+)")
WORK = re.compile(r"SERVER_WORK stage=(\S+) .*?selectedBudgetMs=([\d.,]+) .*?tickP95Ms=([\d.,]+) overBudget=(\d+)")
NAV = re.compile(r"NavPathQuery=(\d+)ms/(\d+)x/max(\d+)ms")


def num(text):
    return float(text.replace(",", "."))


def read_lines(path, tail_mb):
    size = os.path.getsize(path)
    with open(path, "rb") as handle:
        if size > tail_mb * 1024 * 1024:
            handle.seek(size - tail_mb * 1024 * 1024)
            handle.readline()
        for raw in handle:
            yield raw.decode("utf-8", "replace")


def collect(path, tail_mb):
    minutes = []  # one dict per wall-clock minute, in log order
    current, last_key = None, None
    for line in read_lines(path, tail_mb):
        stamp = STAMP.match(line)
        if not stamp or not ("BATTLEGROUP_LOAD" in line or "SERVER_WORK" in line or "BOT_THINK_PROFILE" in line):
            continue
        key = int(stamp.group(1)) * 60 + int(stamp.group(2))
        if key != last_key:
            current = {"at": f"{stamp.group(1)}:{stamp.group(2)}", "groups": 0, "owners": 0, "rvr": 0,
                       "companions": 0, "p95": 0.0, "budget": 0.0, "over": 0, "nav_ms": 0, "nav_calls": 0,
                       "nav_max": 0, "has_work": False}
            minutes.append(current)
            last_key = key
        match = LOAD.search(line)
        if match:
            _, owners, owners_rvr, groups, companions, _ = map(int, match.groups())
            current.update(owners=owners, rvr=owners_rvr, groups=groups, companions=companions)
        match = WORK.search(line)
        if match:
            current["has_work"] = True
            current["budget"] = num(match.group(2))
            current["p95"] = max(current["p95"], num(match.group(3)))
            current["over"] += int(match.group(4))
        match = NAV.search(line) if "BOT_THINK_PROFILE" in line else None
        if match:
            current["nav_ms"] += int(match.group(1))
            current["nav_calls"] += int(match.group(2))
            current["nav_max"] = max(current["nav_max"], int(match.group(3)))
    return [m for m in minutes if m["has_work"]]


def summarize(label, rows):
    if not rows:
        print(f"{label:<34} no minutes")
        return None
    p95 = [r["p95"] for r in rows]
    over = sum(r["over"] for r in rows)
    nav = [r["nav_ms"] for r in rows]
    calls = [r["nav_calls"] for r in rows]
    print(f"{label:<34} {len(rows):>5} min  tick p95 avg {statistics.mean(p95):6.2f} / max {max(p95):6.2f} ms  "
          f"over-budget ticks {over:>5}  pathing {statistics.mean(nav):8.0f} ms/min "
          f"({statistics.mean(calls):8.0f} calls/min, longest {max(r['nav_max'] for r in rows)} ms)")
    return statistics.mean(p95), max(p95), statistics.mean(nav)


def main():
    default = os.path.join(os.environ.get("OFFLINE_DAOC_ROOT", "/mnt/d/Games/OfflineDAoC"),
                           "runtime", "logs", "server-console.log")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("log", nargs="?", default=default)
    parser.add_argument("--min-groups", type=int, default=10, help="companion groups for the 'target' load (default 10)")
    parser.add_argument("--tail", type=int, default=64, help="megabytes of log to read from the end (default 64)")
    args = parser.parse_args()

    rows = collect(args.log, args.tail)
    print(f"{args.log}: {len(rows)} minutes with SERVER_WORK\n")
    with_bg = [r for r in rows if r["groups"] > 0]
    target = [r for r in rows if r["groups"] >= args.min_groups and r["rvr"] >= 2]
    base = summarize("no battlegroup companions", [r for r in rows if r["groups"] == 0])
    summarize("any companion groups in a battlegroup", with_bg)
    loaded = summarize(f"{args.min_groups}+ groups, 2 owners in RvR", target)

    if not target:
        print("\nNo minute matches the target load yet (2 owners in RvR, "
              f"{args.min_groups}+ companion groups). Run that scene for 15-30 minutes and rerun.")
        return 0
    budget = max(r["budget"] for r in target)
    print(f"\nTick budget {budget:.2f} ms. Target minutes: owners/groups/companions at peak "
          f"{max(r['owners'] for r in target)}/{max(r['groups'] for r in target)}/{max(r['companions'] for r in target)}.")
    if base:
        print(f"Cost of the battlegroup: tick p95 {loaded[0] - base[0]:+.2f} ms, pathing {loaded[2] - base[2]:+.0f} ms/min.")
    ok = loaded[1] < budget and sum(r["over"] for r in target) <= len(target)
    print("Verdict:", "within budget" if ok else "over budget: look at the slowest stage in SERVER_WORK / BOT_THINK_SLOW")
    return 0


if __name__ == "__main__":
    sys.exit(main())
