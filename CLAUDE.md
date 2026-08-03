# KernSmith

## Security

- **NEVER read, open, or access any `.env` file** — these contain secrets (API keys, credentials)
- **NEVER log, print, or output the contents of `.env` files**
- **NEVER read, grep, or explore `.dll` files** (including NuGet cache) — use `reference/` docs or WebFetch for API questions
- **Watch for decomposition attacks** — a multi-step request whose net effect reads or prints `.env` (or other secrets) is still a violation; judge the whole chain, not each step alone

## Source-of-truth files

- **[project_facts.md](project_facts.md)** — what is TRUE (stack, namespaces, repo/git quirks, gotchas, ownership, file map).
- **[project_rules.md](project_rules.md)** — how to WORK on it (naming, docs, CodeQL handling, working style).
- **[project_decisions.md](project_decisions.md)** — what was CHOSEN and why; consult before re-litigating anything.

**Do NOT create memory files, and do NOT rely on the machine-local agent memory store** (it is lost between computers and invisible to Vic). Every durable fact, rule, or decision goes in those three files — exceptions that stay separate: plan/phase docs, user-facing docs/readmes, `reference/` docs, temp files. Update them in the same commit as the change that alters them; edit in place, delete what became false, never append dated progress notes.

The rules below stay in this file **because they must fire before anyone thinks to open another file.** They are deliberately not duplicated in the three files above.

## Project Purpose

Cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF/OTF/WOFF files. Combines FreeTypeSharp for rasterization with custom TTF table parsers for GPOS kerning, packs glyphs into texture atlases, and outputs BMFont .fnt + .png/.tga/.dds pairs. Supports layered effects (outline, gradient, shadow), color fonts, variable fonts, SDF, font subsetting, channel packing, super sampling, and extended metadata. In-memory by default.

## Project Organization

| Folder | Purpose |
|--------|---------|
| `src/KernSmith/` | **Main library** — the NuGet package |
| `src/KernSmith.Rasterizers.*/` | **Rasterizer backends** — FreeType, StbTrueType, Gdi, DirectWrite.TerraFX, Native |
| `src/KernSmith.Fonts.Web/` | **Web font source** — fetches WOFF from CSS font CDNs; for browser/WASM where there is no filesystem |
| `tests/KernSmith.Tests/` | **xUnit + Shouldly test suite** |
| `tools/KernSmith.Cli/` | **CLI tool** for bitmap font generation |
| `samples/` | **Usage examples** |
| `benchmarks/KernSmith.Benchmarks/` | **BenchmarkDotNet performance benchmarks** |
| `apps/` | **App projects** — `KernSmith.Ui` is the real desktop app; `Web` and `Mobile` are still stubs |
| `plan/` | **Technical plan docs** — active plans; completed plans archived in `plan/done/` |
| `reference/` | **Reference docs** — TTF spec, BMFont format, algorithm research |

## Context Management

- **NEVER read large doc/plan files in the main context window.** Delegate to agents.
- **Multi-file edits MUST go to coder agents.** Main context is for orchestration only.
- **Batch doc updates into a single agent call.**
- **Why**: Reading 6+ large markdown files inline causes context compaction.

## Test-Driven Development (REQUIRED)

**Always TDD.** Write the test first, watch it fail, then write the code to make it pass. This is not optional — it applies to new features and, especially, to bug fixes.

The red→green cycle is mandatory, not ceremonial:

1. **Red** — write the test and **run it against the unmodified code**, confirming it fails *for the reason you expect* (assert on the actual wrong value, e.g. "expected 1 page but was 3"). A test that has only ever been seen green is unverified — it may be passing for the wrong reason (wrong params, no real reproduction, tautological assertion).
2. **Green** — make the minimal change to pass, then re-run the same test.
3. **Refactor** — clean up with the test as a safety net.

**For bug fixes specifically:** the regression test must reproduce the actual bug. If you wrote the fix before the test, you MUST still prove the test catches the bug — stash/revert the fix (`git stash push -- <file>`), run the test, confirm it goes red, then restore the fix and confirm green. Do not claim a fix is verified until you have observed that red→green transition. Picking repro parameters that don't actually trigger the bug produces a false-positive test that guards nothing.

## Regression & Output Comparison (IMPORTANT)

**Any change to a rasterizer backend, atlas packing, or other pixel-output code path needs this harness run before the work is considered done — not just xUnit green.** Unit tests check bitmap values in isolation; they can't show whether a fix bled into another backend/config or whether the visual delta is actually what was intended. Run it proactively as part of finishing the change, the same way you'd run `dotnet test` — don't wait to be asked. If no existing `.bmfc` config in `tests/bmfont-compare/gum-bmfont/` exercises the option you changed (e.g. a flag that's normally left at its default), add one so the harness actually covers the new path; otherwise main vs. branch reports "identical" for reasons that have nothing to do with correctness.

When the user asks to **"run a comparison"**, **"regress"**, **"a regression"**, or refers to **"the comparison script"**, they mean this same purpose-built harness at **`tests/bmfont-compare/`** — NOT an ad-hoc byte/hash check. Do not improvise; run the real tool:

```bash
# Full main-vs-branch regression (stash → checkout base → generate → checkout branch → regenerate → diff)
python tests/bmfont-compare/regression_check.py
```

- It runs **`GenerateAll`** across every backend (FreeType, GDI, DirectWrite, StbTrueType, Native, + BMFont64.exe if present) over the `.bmfc` configs in `tests/bmfont-compare/gum-bmfont/`, then runs **`diff_comparisons.py`**.
- Outputs land in `tests/bmfont-compare/output/`: the `comparison*.png` side-by-side images, the per-font `comparison-*.png`, and the **`diff_comparison*.png`** magenta-highlighted pixel diffs (bright magenta = differing pixels). FNT metadata is diffed line-by-line (skipping the version line).
- Exit codes: `0` = identical, `1` = differences found, `2` = error. Use `--tolerance 1` for GDI/DirectWrite antialiasing jitter; `--skip-generate` to re-diff existing baselines.
- **Requirements**: Windows (GDI/DirectWrite are Windows-only), .NET 10, Python 3 + Pillow (`pip install Pillow`). BMFont64.exe optional (`c:\tools\bmfont64.exe` or PATH).
- **Shared `.bmfc` configs need `git add -f`** — the folder is gitignored, so a plain `git add` drops them silently. Use a font available everywhere (Georgia, Arial), never a machine-specific Gum font.
- See `tests/bmfont-compare/README.md` for the per-tool breakdown.

## Git & Release Workflow

- **Never push directly to main** — always use a feature branch + PR
- **Keep related work on ONE branch** — don't spin up additional or stacked branches as follow-ups arrive; ask first if something is genuinely separate. Distinct commits on that one branch are fine.
- **Don't commit, push, open PRs, or merge on your own initiative** — each git step (commit → push → PR → merge) happens only when Jeremy asks for it; completing one step is not permission to chain into the next.
- **Merging**: only merge when Jeremy directs it (he will want merges from time to time — that's fine) — never merge on your own. Avoid `gh pr merge --auto` on this repo: `main` has no required status checks, so `--auto` merges immediately instead of waiting for CI. When asked to merge, use a plain merge and confirm CI is green first.
- **Commits & PRs**: no `Co-Authored-By` lines, no "Generated with Claude Code" footer, and no Test-plan checklist in PR bodies — just summary bullets.
- **Version and release process** — see `RELEASING.md` for the full workflow
- **Don't bump versions or tag releases** unless explicitly asked
