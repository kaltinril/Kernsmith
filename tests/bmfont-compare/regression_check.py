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
"""

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

# File patterns to copy as baseline (prefixed with main_).
BASELINE_PATTERNS = [
    "comparison*.png",
    "Font*.png",
    "plain*.png",
    "fire*.png",
    "mix*.png",
    "*.fnt",
]


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
    """Stash uncommitted changes."""
    run_cmd(["git", "stash", "push", "-m", "regression_check: auto-stash"])


def stash_pop():
    """Pop the most recent stash."""
    run_cmd(["git", "stash", "pop"])


def checkout_branch(branch):
    """Checkout a git branch."""
    run_cmd(["git", "checkout", branch])


def generate_fonts(output_dir, repo_root):
    """Run the GenerateAll dotnet project to produce bitmap fonts."""
    project = repo_root / "tests" / "bmfont-compare" / "GenerateAll"
    bmfont_dir = repo_root / "tests" / "bmfont-compare" / "gum-bmfont"
    run_cmd([
        "dotnet", "run",
        "--project", str(project),
        "--framework", "net10.0-windows",
        "--",
        str(bmfont_dir),
        str(output_dir),
    ])


def copy_baselines(output_dir, prefix="main_"):
    """Copy generated files to prefixed baseline versions."""
    output_path = Path(output_dir)
    copied = 0

    for pattern in BASELINE_PATTERNS:
        for src in output_path.glob(pattern):
            # Don't copy files that already have the prefix.
            if src.name.startswith(prefix):
                continue
            dest = output_path / f"{prefix}{src.name}"
            shutil.copy2(src, dest)
            copied += 1

    print(f"  Copied {copied} files with prefix '{prefix}'")


def run_diff(output_dir, repo_root, tolerance):
    """Run diff_comparisons.py and return its exit code."""
    diff_script = repo_root / "tests" / "bmfont-compare" / "diff_comparisons.py"
    cmd = [sys.executable, str(diff_script), "--dir", str(output_dir)]
    if tolerance is not None:
        cmd.extend(["--tolerance", str(tolerance)])

    cmd_str = " ".join(str(a) for a in cmd)
    print(f"  $ {cmd_str}")
    result = subprocess.run(cmd, check=False)
    return result.returncode


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

    if args.skip_generate:
        print("=== Skipping generation (--skip-generate) ===")
        print()
        print("=== Running diff ===")
        exit_code = run_diff(output_dir, repo_root, args.tolerance)
        print()
        if exit_code == 0:
            print("RESULT: All comparisons passed.")
        else:
            print(f"RESULT: Differences detected (exit code {exit_code}).")
        sys.exit(exit_code)

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

        # Step 2: Generate baseline on base branch.
        print(f"=== Step 2: Generate baseline on '{base_branch}' ===")
        checkout_branch(base_branch)
        did_checkout = True
        generate_fonts(output_dir, repo_root)
        print()

        # Copy outputs to main_ prefixed versions.
        print("=== Step 3: Copy baseline files ===")
        copy_baselines(output_dir)
        print()

        # Step 4: Generate current on feature branch.
        print(f"=== Step 4: Generate current on '{feature_branch}' ===")
        checkout_branch(feature_branch)
        did_checkout = False  # We're back on the target branch now.

        if did_stash:
            print("  Restoring stashed changes...")
            stash_pop()
            did_stash = False

        generate_fonts(output_dir, repo_root)
        print()

        # Step 5: Run diff.
        print("=== Step 5: Running diff ===")
        exit_code = run_diff(output_dir, repo_root, args.tolerance)
        print()

        if exit_code == 0:
            print("RESULT: All comparisons passed.")
        else:
            print(f"RESULT: Differences detected (exit code {exit_code}).")

        sys.exit(exit_code)

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
