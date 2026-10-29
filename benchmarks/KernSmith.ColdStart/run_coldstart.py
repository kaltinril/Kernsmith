#!/usr/bin/env python3
"""Cold-start driver for KernSmith.ColdStart.

Publishes the tool once per build mode (JIT, ReadyToRun, NativeAOT), launches K fresh processes
per backend x mode (round-robin, so drift over the run hits every combination alike), collects
each process's JSON report and prints/saves a summary table plus CSVs.

    python benchmarks/KernSmith.ColdStart/run_coldstart.py
    python benchmarks/KernSmith.ColdStart/run_coldstart.py --runs 2 --repeat 5 --backends native,freetype

Exit codes: 0 = every publish and run succeeded, 1 = something failed (recorded in the
summary), 2 = bad arguments.
"""

import argparse
import csv
import datetime
import json
import os
import platform
import shutil
import statistics
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
PROJECT = HERE / "KernSmith.ColdStart.csproj"
DEFAULT_FONT = REPO / "tests" / "KernSmith.Tests" / "Fixtures" / "Roboto-Regular.ttf"
IS_WINDOWS = sys.platform == "win32"

MODES = {
    "jit": ["--self-contained", "false", "-p:PublishReadyToRun=false"],
    "r2r": ["--self-contained", "false", "-p:PublishReadyToRun=true"],
    "aot": ["-p:PublishAot=true"],
}
PORTABLE_BACKENDS = ["native", "freetype", "stbtruetype"]
WINDOWS_BACKENDS = ["gdi", "directwrite"]

# Steady state = median of calls 30..50 (1-based) when there are enough calls.
STEADY_FIRST, STEADY_LAST = 30, 50


def runtime_identifier():
    machine = platform.machine().lower()
    arch = "arm64" if machine in ("arm64", "aarch64") else "x64"
    if IS_WINDOWS:
        return f"win-{arch}"
    if sys.platform == "darwin":
        return f"osx-{arch}"
    return f"linux-{arch}"


def tail(text, lines=15):
    return "\n".join((text or "").strip().splitlines()[-lines:])


def publish_env():
    """NativeAOT links through the VS C++ tools. Visual Studio's vcvarsall.bat runs vswhere.exe by
    bare name, and when the VS Installer folder is not on PATH its error text ends up in the
    linker path ILCompiler captures (MSB3073 on link.exe), so put the folder on PATH."""
    env = dict(os.environ)
    if IS_WINDOWS and shutil.which("vswhere") is None:
        installer = Path(env.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio" / "Installer"
        if (installer / "vswhere.exe").is_file():
            env["PATH"] = f"{installer}{os.pathsep}{env.get('PATH', '')}"
    return env


def publish(mode, out_dir, tfm, rid, timeout):
    """Publishes one mode; returns (ok, message)."""
    cmd = ["dotnet", "publish", str(PROJECT), "-c", "Release", "-f", tfm, "-r", rid,
           "-o", str(out_dir)] + MODES[mode]
    print(f"[publish] {mode}: {' '.join(cmd)}", flush=True)
    start = time.perf_counter()
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, cwd=REPO, env=publish_env())
    except subprocess.TimeoutExpired:
        return False, f"publish timed out after {timeout}s"
    elapsed = time.perf_counter() - start
    output = (proc.stdout or "") + (proc.stderr or "")
    (out_dir.parent / f"publish-{mode}.log").write_text(output, encoding="utf-8")
    if proc.returncode != 0:
        errors = [line.strip() for line in output.splitlines() if ": error " in line]
        return False, "\n".join(dict.fromkeys(errors)) or tail(output)
    return True, f"published in {elapsed:.0f}s"


def executable(publish_dir):
    return publish_dir / ("KernSmith.ColdStart.exe" if IS_WINDOWS else "KernSmith.ColdStart")


def run_once(exe, mode, backend, run_index, args, results_dir):
    """Launches one fresh process; returns a run record (never raises)."""
    report_path = results_dir / "runs" / f"{mode}-{backend}-{run_index:03d}.json"
    cmd = [str(exe), "--backend", backend, "--font", str(args.font), "--size", str(args.size),
           "--charset", args.charset, "--repeat", str(args.repeat), "--mode", mode,
           "--out", str(report_path)]
    record = {"mode": mode, "backend": backend, "run": run_index, "ok": False,
              "exit_code": None, "wall_ms": None, "error": None, "report": None}
    if report_path.exists():
        report_path.unlink()
    start = time.perf_counter()
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=args.timeout)
    except subprocess.TimeoutExpired:
        record["error"] = f"timed out after {args.timeout}s"
        return record
    except OSError as ex:
        record["error"] = f"could not start: {ex}"
        return record
    record["wall_ms"] = (time.perf_counter() - start) * 1000
    record["exit_code"] = proc.returncode

    if report_path.exists():
        try:
            record["report"] = json.loads(report_path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as ex:
            record["error"] = f"unreadable report: {ex}"
    report = record["report"]
    if report is not None and report.get("ok") and proc.returncode == 0:
        record["ok"] = True
    elif record["error"] is None:
        detail = (report or {}).get("error") or tail(proc.stderr, 5) or tail(proc.stdout, 5)
        record["error"] = f"exit {proc.returncode}: {detail or 'no report written'}"
    return record


def median(values):
    return statistics.median(values) if values else None


def steady_slice(calls):
    """Calls 30..50 when available, else the second half of the calls after the first."""
    if len(calls) >= STEADY_LAST:
        return calls[STEADY_FIRST - 1:STEADY_LAST]
    rest = calls[1:]
    return rest[len(rest) // 2:] if rest else []


def summarize(records, modes, backends, publish_status):
    rows = []
    for mode in modes:
        for backend in backends:
            runs = [r for r in records if r["mode"] == mode and r["backend"] == backend]
            ok = [r for r in runs if r["ok"]]
            row = {"backend": backend, "mode": mode, "ok_runs": len(ok), "runs": len(runs)}
            if not publish_status[mode][0]:
                row["status"] = "publish failed"
            elif not runs:
                row["status"] = "not run"
            elif not ok:
                row["status"] = "failed: " + (runs[0]["error"] or "").splitlines()[0][:120]
            else:
                row["status"] = "ok" if len(ok) == len(runs) else f"{len(runs) - len(ok)} failed"
                reports = [r["report"] for r in ok]
                first = [rep["callsMs"][0] for rep in reports]
                second = [rep["callsMs"][1] for rep in reports if len(rep["callsMs"]) > 1]
                steady = [median(steady_slice(rep["callsMs"])) for rep in reports]
                steady = [s for s in steady if s is not None]
                start_to_first = [rep["processStartToMainMs"] + rep["mainToFirstResultMs"] for rep in reports
                                  if rep.get("processStartToMainMs") is not None]
                # Wall clock minus what ran after the first result: an upper bound (it includes
                # process teardown) that does not depend on the OS process start time.
                wall_to_first = [r["wall_ms"] - (r["report"]["mainToEndMs"] - r["report"]["mainToFirstResultMs"])
                                 for r in ok]
                jit_first = [rep["jitAfterFirstCall"] for rep in reports if rep.get("jitAfterFirstCall")]
                row.update({
                    "first_median_ms": median(first),
                    "first_worst_ms": max(first),
                    "second_median_ms": median(second),
                    "steady_median_ms": median(steady),
                    "first_steady_ratio": (median(first) / median(steady)) if steady and median(steady) else None,
                    "start_to_first_median_ms": median(start_to_first),
                    "launch_to_first_wall_median_ms": median(wall_to_first),
                    "jit_methods_after_first": median([j["methods"] for j in jit_first]),
                    "jit_compile_ms_after_first": median([j["compileMs"] for j in jit_first]),
                })
            rows.append(row)
    return rows


SUMMARY_COLUMNS = [
    ("backend", "backend", "{}"), ("mode", "mode", "{}"), ("runs", "ok/runs", None),
    ("first_median_ms", "1st med", "{:.1f}"), ("first_worst_ms", "1st worst", "{:.1f}"),
    ("second_median_ms", "2nd med", "{:.1f}"), ("steady_median_ms", "steady", "{:.2f}"),
    ("first_steady_ratio", "1st/steady", "{:.0f}x"), ("start_to_first_median_ms", "start->1st", "{:.0f}"),
    ("launch_to_first_wall_median_ms", "launch->1st(wall)", "{:.0f}"),
    ("jit_methods_after_first", "JIT methods", "{:.0f}"), ("jit_compile_ms_after_first", "JIT ms", "{:.0f}"),
    ("status", "status", "{}"),
]


def format_table(rows):
    def cell(row, key, fmt):
        if key == "runs":
            return f"{row['ok_runs']}/{row['runs']}"
        value = row.get(key)
        return "-" if value is None else fmt.format(value)

    table = [[title for _, title, _ in SUMMARY_COLUMNS]]
    table += [[cell(row, key, fmt) for key, _, fmt in SUMMARY_COLUMNS] for row in rows]
    widths = [max(len(r[i]) for r in table) for i in range(len(SUMMARY_COLUMNS))]
    lines = ["  ".join(v.rjust(w) if i not in (0, 1, len(widths) - 1) else v.ljust(w)
                       for i, (v, w) in enumerate(zip(r, widths))) for r in table]
    lines.insert(1, "  ".join("-" * w for w in widths))
    return "\n".join(lines)


def write_csvs(results_dir, rows, records):
    keys = ["backend", "mode", "ok_runs", "runs", "first_median_ms", "first_worst_ms", "second_median_ms",
            "steady_median_ms", "first_steady_ratio", "start_to_first_median_ms",
            "launch_to_first_wall_median_ms", "jit_methods_after_first", "jit_compile_ms_after_first", "status"]
    with open(results_dir / "summary.csv", "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=keys, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)

    with open(results_dir / "runs.csv", "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["mode", "backend", "run", "ok", "exit_code", "wall_ms", "process_start_to_main_ms",
                         "main_to_first_result_ms", "first_call_ms", "second_call_ms", "jit_methods_after_first",
                         "jit_compile_ms_after_first", "calls_ms", "error"])
        for r in records:
            rep = r["report"] or {}
            calls = rep.get("callsMs") or []
            jit = rep.get("jitAfterFirstCall") or {}
            writer.writerow([r["mode"], r["backend"], r["run"], r["ok"], r["exit_code"], r["wall_ms"],
                             rep.get("processStartToMainMs"), rep.get("mainToFirstResultMs"),
                             calls[0] if calls else None, calls[1] if len(calls) > 1 else None,
                             jit.get("methods"), jit.get("compileMs"),
                             " ".join(f"{c:.3f}" for c in calls), r["error"]])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    default_backends = PORTABLE_BACKENDS + (WINDOWS_BACKENDS if IS_WINDOWS else [])
    parser.add_argument("--modes", default="jit,r2r,aot", help="comma list of jit, r2r, aot")
    parser.add_argument("--backends", default=",".join(default_backends), help="comma list of backends")
    parser.add_argument("--runs", type=int, default=20, help="fresh processes per backend x mode (K)")
    parser.add_argument("--repeat", type=int, default=50, help="Generate calls per process (N)")
    parser.add_argument("--font", type=Path, default=DEFAULT_FONT)
    parser.add_argument("--size", type=float, default=32)
    parser.add_argument("--charset", default="ascii", help="ascii, extended, latin, or literal characters")
    parser.add_argument("--timeout", type=int, default=300, help="seconds per run")
    parser.add_argument("--publish-timeout", type=int, default=1800, help="seconds per publish")
    parser.add_argument("--publish-root", type=Path, default=HERE / "bin" / "coldstart-publish")
    parser.add_argument("--results-dir", type=Path, default=None,
                        help="default: bin/coldstart-results/<timestamp>")
    parser.add_argument("--skip-publish", action="store_true", help="reuse existing publish folders")
    args = parser.parse_args()

    modes = [m.strip().lower() for m in args.modes.split(",") if m.strip()]
    backends = [b.strip().lower() for b in args.backends.split(",") if b.strip()]
    if any(m not in MODES for m in modes) or not modes or not backends or args.runs < 1 or args.repeat < 1:
        parser.error("modes must be from jit,r2r,aot; backends non-empty; runs and repeat >= 1")
    if not args.font.is_file():
        parser.error(f"font not found: {args.font}")
    args.font = args.font.resolve()

    tfm = "net10.0-windows" if IS_WINDOWS else "net10.0"
    rid = runtime_identifier()
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    results_dir = (args.results_dir or HERE / "bin" / "coldstart-results" / stamp).resolve()
    (results_dir / "runs").mkdir(parents=True, exist_ok=True)
    args.publish_root.mkdir(parents=True, exist_ok=True)

    publish_status = {}
    for mode in modes:
        publish_dir = args.publish_root / mode
        if args.skip_publish:
            ok = executable(publish_dir).is_file()
            publish_status[mode] = (ok, "reused" if ok else f"no executable in {publish_dir}")
        else:
            publish_status[mode] = publish(mode, publish_dir, tfm, rid, args.publish_timeout)
        print(f"[publish] {mode}: {'OK' if publish_status[mode][0] else 'FAILED'} - {publish_status[mode][1]}",
              flush=True)

    records = []
    runnable = [m for m in modes if publish_status[m][0]]
    for run_index in range(1, args.runs + 1):
        for mode in runnable:
            for backend in backends:
                record = run_once(executable(args.publish_root / mode), mode, backend, run_index, args, results_dir)
                records.append(record)
                first = record["report"]["callsMs"][0] if record["ok"] else None
                status = f"first {first:.1f} ms" if first is not None else f"FAILED {record['error']}"
                print(f"[run {run_index}/{args.runs}] {mode:3} {backend:11} {status}", flush=True)

    rows = summarize(records, modes, backends, publish_status)
    table = format_table(rows)
    header = (f"KernSmith cold start: {args.font.name} {args.size:g}px charset={args.charset} "
              f"K={args.runs} N={args.repeat} rid={rid} ({platform.platform()})\n"
              f"steady = median of calls {STEADY_FIRST}-{STEADY_LAST} per process "
              f"(fewer calls: second half after the first); times in ms, medians across processes")
    publish_lines = "\n".join(f"publish {m}: {'OK' if s[0] else 'FAILED'} - {s[1]}" for m, s in publish_status.items())
    text = f"{header}\n\n{table}\n\n{publish_lines}\n"
    print("\n" + text)

    (results_dir / "summary.txt").write_text(text, encoding="utf-8")
    write_csvs(results_dir, rows, records)
    (results_dir / "summary.json").write_text(json.dumps(
        {"publish": {m: {"ok": s[0], "message": s[1]} for m, s in publish_status.items()}, "rows": rows},
        indent=2), encoding="utf-8")
    print(f"Results: {results_dir}")

    failed = any(not s[0] for s in publish_status.values()) or any(not r["ok"] for r in records)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
