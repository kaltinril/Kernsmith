"""Automated main-vs-branch regression comparison workflow.

Checks out the base branch, generates baseline bitmap fonts, checks out the
feature branch, generates current bitmap fonts, then runs diff_comparisons.py
to detect regressions.

Usage:
    # Compare current branch against main:
    python regression_check.py

    # Compare a specific branch against main:
    python regression_check.py --branch feature/my-change

    # Skip generation (re-run diff only):
    python regression_check.py --skip-generate

    # With pixel tolerance:
    python regression_check.py --tolerance 1

Before the baseline is generated every *.fnt / *.png in the output directory is
deleted, and the baseline is MOVED to main_-prefixed names, so a (config,
backend) pair the branch fails to produce can never be compared against a stale
file from an earlier run or from the base.

Configs (.bmfc in gum-bmfont/) present only on the branch are reported as NEW
(no baseline) with the backends that produced output; configs present only on
the base are REMOVED. Neither fails the run.

Exit codes:
    0 = everything produced on both sides is identical; failures on BOTH sides
        (capability skips, CFF on a TrueType-only backend, ...) are tolerated
    1 = differences found, or a (config, backend) pair that produced output on
        the base failed on the branch
    2 = error (build failure, git failure, nothing could be compared)
"""

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

BASELINE_PREFIX = "main_"

# Generated file types. Everything matching these in the output dir is cleared
# before a run and moved to BASELINE_PREFIX after the base generation.
GENERATED_PATTERNS = ("*.fnt", "*.png")

# Backend suffixes GenerateAll writes: {config}-{backend}.fnt.
KNOWN_BACKENDS = ("freetype", "gdi", "directwrite", "stbtruetype", "native", "bmfont")

# Remembers the config sets of the last full run so --skip-generate can still tell
# REMOVED configs and branch-only failures apart.
STATE_FILE = "regression_state.json"


def run_cmd(args, description=None, check=True, capture=False):
    """Run a subprocess command, printing it first."""
    cmd_str = " ".join(str(a) for a in args)
    if description:
        print(f"  {description}")
    print(f"  $ {cmd_str}")
    result = subprocess.run(
        args,
        check=check,
        capture_output=capture,
        text=True if capture else None,
    )
    return result


def detect_repo_root():
    """Detect the git repo root directory."""
    result = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        capture_output=True,
        text=True,
        check=True,
    )
    return Path(result.stdout.strip())


def get_current_branch():
    """Get the current git branch name."""
    result = subprocess.run(
        ["git", "branch", "--show-current"],
        capture_output=True,
        text=True,
        check=True,
    )
    return result.stdout.strip()


def has_uncommitted_changes():
    """Check if there are uncommitted changes."""
    result = subprocess.run(
        ["git", "status", "--porcelain"],
        capture_output=True,
        text=True,
        check=True,
    )
    return len(result.stdout.strip()) > 0


def stash_changes():
    """Stash uncommitted changes, including untracked files.

    --include-untracked matters: a feature branch's NEW source files would
    otherwise remain in the tree on the base branch and poison the baseline
    build with a hybrid of old tracked + new untracked code. (Gitignored
    files, e.g. the output/ dir, are not touched.)
    """
    run_cmd(["git", "stash", "push", "--include-untracked", "-m", "regression_check: auto-stash"])


def stash_pop():
    """Pop the most recent stash."""
    run_cmd(["git", "stash", "pop"])


def checkout_branch(branch):
    """Checkout a git branch."""
    run_cmd(["git", "checkout", branch])


def generate_fonts(output_dir, repo_root):
    """Run the GenerateAll dotnet project to produce bitmap fonts.

    Returns {(config, backend): message} for every pair that FAILED. A nonzero exit
    only means some pair failed -- normally a backend declining something the config
    asks for, which then fails on both sides. The caller compares these failures
    with what the base produced; a wholesale failure surfaces as MISSING files.
    """
    project = repo_root / "tests" / "bmfont-compare" / "GenerateAll"
    bmfont_dir = repo_root / "tests" / "bmfont-compare" / "gum-bmfont"

    # Build separately and keep it fatal. 'dotnet run' exits 1 for both a build
    # failure and a partial generation, and swallowing the former would leave
    # stale output in place for the diff to compare against itself -- a silent
    # pass that proves nothing.
    run_cmd([
        "dotnet", "build", str(project),
        "--framework", "net10.0-windows",
        "--nologo", "-v", "q",
    ])

    args = [
        "dotnet", "run",
        "--project", str(project),
        "--framework", "net10.0-windows",
        "--no-build",
        "--",
        str(bmfont_dir),
        str(output_dir),
    ]
    print(f"  $ {' '.join(args)}")
    # Stream the output and keep it, so per-pair failures can be reported with their reason.
    lines = []
    with subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, encoding="utf-8", errors="replace") as proc:
        for line in proc.stdout:
            print(line, end="")
            lines.append(line.rstrip("\r\n"))
        returncode = proc.wait()

    failures = parse_generate_output(lines)
    if returncode != 0:
        print(f"  NOTE: {len(failures)} (config, backend) pair(s) failed to generate "
              f"(exit {returncode}); compared with the base below.")
    return failures


def parse_generate_output(lines):
    """Collect {(config, backend): message} for each 'FAILED' line in GenerateAll output."""
    failures = {}
    config = None
    header = re.compile(r"^--- (.+) ---$")
    pair = re.compile(r"^\s+(\S+) \.\.\. (.*)$")
    for line in lines:
        if line.startswith("=== Generating comparison images"):
            break  # comparison-image lines use the same "name ... status" shape
        m = header.match(line.strip())
        if m:
            config = m.group(1)
            continue
        m = pair.match(line)
        if m and config and m.group(1) in KNOWN_BACKENDS and m.group(2).startswith("FAILED"):
            failures[(config, m.group(1))] = m.group(2)[len("FAILED:"):].strip()
    return failures


def list_configs(bmfc_dir):
    """Names of the .bmfc configs currently in bmfc_dir (what GenerateAll will render)."""
    return {p.stem for p in Path(bmfc_dir).glob("*.bmfc")}


def clean_generated_outputs(output_dir):
    """Delete every generated file (current, main_ baselines, diff_ images) from output_dir."""
    output_path = Path(output_dir)
    removed = 0
    for pattern in GENERATED_PATTERNS:
        for path in output_path.glob(pattern):
            path.unlink()
            removed += 1
    (output_path / STATE_FILE).unlink(missing_ok=True)
    print(f"  Cleared {removed} generated files from {output_path}")
    return removed


def promote_baselines(output_dir, prefix=BASELINE_PREFIX):
    """Rename every generated file to its prefixed baseline name.

    Moving (not copying) leaves the unprefixed names empty for the branch run, so a
    pair the branch fails to produce shows up as MISSING instead of comparing the
    base's own file with itself.
    """
    output_path = Path(output_dir)
    moved = 0
    for pattern in GENERATED_PATTERNS:
        for src in sorted(output_path.glob(pattern)):
            if src.name.startswith(prefix) or src.name.startswith("diff_"):
                continue
            src.replace(output_path / f"{prefix}{src.name}")
            moved += 1
    print(f"  Moved {moved} files to prefix '{prefix}'")
    return moved


def backends_with_output(output_dir, config):
    """Backends that wrote {config}-{backend}.fnt into output_dir."""
    return [b for b in KNOWN_BACKENDS if (Path(output_dir) / f"{config}-{b}.fnt").exists()]


def find_branch_only_failures(output_dir, branch_failures, removed_configs, prefix=BASELINE_PREFIX):
    """(config, backend, message) for pairs that failed on the branch but produced output on the base."""
    regressions = []
    for (config, backend), message in sorted(branch_failures.items()):
        if config in removed_configs:
            continue
        if (Path(output_dir) / f"{prefix}{config}-{backend}.fnt").exists():
            regressions.append((config, backend, message))
    return regressions


def save_state(output_dir, base_configs, branch_configs, branch_failures):
    state = {
        "base_configs": sorted(base_configs),
        "branch_configs": sorted(branch_configs),
        "branch_failures": [[c, b, m] for (c, b), m in sorted(branch_failures.items())],
    }
    (Path(output_dir) / STATE_FILE).write_text(json.dumps(state, indent=2), encoding="utf-8")


def load_state(output_dir):
    path = Path(output_dir) / STATE_FILE
    if not path.exists():
        return None
    state = json.loads(path.read_text(encoding="utf-8"))
    return (set(state["base_configs"]), set(state["branch_configs"]),
            {(c, b): m for c, b, m in state["branch_failures"]})


def report_summary(output_dir, base_configs, branch_configs, branch_failures):
    """Print config-level NEW/REMOVED and branch-only failures; returns the regression list."""
    new_configs = sorted(branch_configs - base_configs)
    removed_configs = set(base_configs - branch_configs)
    regressions = find_branch_only_failures(output_dir, branch_failures, removed_configs)
    tolerated = len(branch_failures) - len(regressions)

    print("=== Summary ===")
    print(f"  Configs: {len(base_configs)} on base, {len(branch_configs)} on branch")
    for config in new_configs:
        backends = backends_with_output(output_dir, config)
        produced = ", ".join(backends) if backends else "no backend produced output"
        print(f"  NEW config '{config}' (no baseline): {produced}")
    for config in sorted(removed_configs):
        print(f"  REMOVED config '{config}' (not on branch)")
    if regressions:
        print(f"  FAIL: {len(regressions)} pair(s) produced output on the base but failed on the branch:")
        for config, backend, message in regressions:
            print(f"    {config} / {backend}: {message}")
    print(f"  Branch failures where the base produced nothing either (tolerated): {tolerated}")
    print()
    return regressions


def final_exit_code(diff_exit, regressions):
    """2 if the diff errored; 1 for any difference or branch-only failure; else 0."""
    if diff_exit == 2:
        return 2
    if diff_exit != 0 or regressions:
        return 1
    return 0


def run_diff(output_dir, repo_root, tolerance, removed_configs=()):
    """Run diff_comparisons.py and return its exit code."""
    diff_script = repo_root / "tests" / "bmfont-compare" / "diff_comparisons.py"
    cmd = [sys.executable, str(diff_script), "--dir", str(output_dir)]
    if tolerance is not None:
        cmd.extend(["--tolerance", str(tolerance)])
    for config in sorted(removed_configs):
        cmd.extend(["--removed-config", config])

    cmd_str = " ".join(str(a) for a in cmd)
    print(f"  $ {cmd_str}")
    result = subprocess.run(cmd, check=False)
    return result.returncode


def finish(exit_code):
    """Print the overall result and exit."""
    if exit_code == 0:
        print("RESULT: All comparisons passed.")
    elif exit_code == 1:
        print("RESULT: Differences detected (exit code 1).")
    else:
        print(f"RESULT: Error (exit code {exit_code}).")
    sys.exit(exit_code)


def main():
    parser = argparse.ArgumentParser(
        description="Automated main-vs-branch regression comparison"
    )
    parser.add_argument(
        "--base", default="main",
        help="Base branch to compare against (default: main)",
    )
    parser.add_argument(
        "--branch", default=None,
        help="Feature branch to test (default: current branch)",
    )
    parser.add_argument(
        "--output", default=None,
        help="Output directory (default: tests/bmfont-compare/output)",
    )
    parser.add_argument(
        "--tolerance", type=int, default=None,
        help="Per-channel pixel tolerance passed to diff_comparisons.py",
    )
    parser.add_argument(
        "--skip-generate", action="store_true",
        help="Skip font generation, only run the diff step",
    )
    args = parser.parse_args()

    # Detect repo root and cd into it.
    repo_root = detect_repo_root()
    os.chdir(repo_root)
    print(f"Repo root: {repo_root}")

    # Resolve output directory.
    if args.output:
        output_dir = Path(args.output).resolve()
    else:
        output_dir = repo_root / "tests" / "bmfont-compare" / "output"
    output_dir.mkdir(parents=True, exist_ok=True)

    # Resolve the feature branch.
    original_branch = get_current_branch()
    feature_branch = args.branch if args.branch else original_branch
    base_branch = args.base

    print(f"Base branch:    {base_branch}")
    print(f"Feature branch: {feature_branch}")
    print(f"Output dir:     {output_dir}")
    print()

    bmfc_dir = repo_root / "tests" / "bmfont-compare" / "gum-bmfont"
    if output_dir.resolve() == bmfc_dir.resolve():
        # The source folder holds BMFont reference .fnt/.png files the cleanup would delete.
        print("ERROR: --output must not be the gum-bmfont config folder.", file=sys.stderr)
        sys.exit(2)

    if args.skip_generate:
        print("=== Skipping generation (--skip-generate) ===")
        print()
        state = load_state(output_dir)
        removed = (state[0] - state[1]) if state else set()
        print("=== Running diff ===")
        diff_exit = run_diff(output_dir, repo_root, args.tolerance, removed)
        print()
        regressions = report_summary(output_dir, *state) if state else []
        finish(final_exit_code(diff_exit, regressions))

    # Track state for cleanup.
    did_stash = False
    did_checkout = False

    try:
        # Step 1: Save state.
        print("=== Step 1: Saving state ===")
        if has_uncommitted_changes():
            print("  Uncommitted changes detected, stashing...")
            stash_changes()
            did_stash = True
        else:
            print("  Working tree is clean.")
        print()

        # Step 2: Generate baseline on base branch, from an empty output dir.
        print(f"=== Step 2: Generate baseline on '{base_branch}' ===")
        checkout_branch(base_branch)
        did_checkout = True
        base_configs = list_configs(bmfc_dir)
        clean_generated_outputs(output_dir)
        generate_fonts(output_dir, repo_root)
        print()

        # Move outputs to main_ prefixed names (leaves the unprefixed names empty).
        print("=== Step 3: Move baseline files ===")
        promote_baselines(output_dir)
        print()

        # Step 4: Generate current on feature branch.
        print(f"=== Step 4: Generate current on '{feature_branch}' ===")
        checkout_branch(feature_branch)
        did_checkout = False  # We're back on the target branch now.

        if did_stash:
            print("  Restoring stashed changes...")
            stash_pop()
            did_stash = False

        branch_configs = list_configs(bmfc_dir)
        branch_failures = generate_fonts(output_dir, repo_root)
        save_state(output_dir, base_configs, branch_configs, branch_failures)
        print()

        # Step 5: Run diff.
        print("=== Step 5: Running diff ===")
        diff_exit = run_diff(output_dir, repo_root, args.tolerance, base_configs - branch_configs)
        print()

        regressions = report_summary(output_dir, base_configs, branch_configs, branch_failures)
        finish(final_exit_code(diff_exit, regressions))

    except subprocess.CalledProcessError as e:
        print(f"\nERROR: Command failed with exit code {e.returncode}", file=sys.stderr)
        sys.exit(2)
    except KeyboardInterrupt:
        print("\nInterrupted by user.", file=sys.stderr)
        sys.exit(2)
    finally:
        # Restore original branch and stash state on any failure.
        current = get_current_branch()
        if current != original_branch and did_checkout:
            print(f"\n  Restoring original branch '{original_branch}'...")
            try:
                subprocess.run(
                    ["git", "checkout", original_branch],
                    check=True,
                )
            except subprocess.CalledProcessError:
                print(
                    f"  WARNING: Failed to restore branch '{original_branch}'.",
                    file=sys.stderr,
                )

        if did_stash:
            print("  Restoring stashed changes...")
            try:
                subprocess.run(["git", "stash", "pop"], check=True)
            except subprocess.CalledProcessError:
                print(
                    "  WARNING: Failed to pop stash. Run 'git stash pop' manually.",
                    file=sys.stderr,
                )


if __name__ == "__main__":
    main()
