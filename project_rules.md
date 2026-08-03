# Project Rules

How to work on this project. One short rule per line. These OVERRIDE default behavior.

## Git & Branching

- Keep related work on **ONE** branch. Do not spin up additional or stacked branches as follow-up sub-tasks arrive.
- If a genuinely separate change comes up, ASK whether it should be its own branch rather than assuming.
- Distinct commits on that one branch are fine and good for review — it is extra *branches* to avoid.
- Never push directly to main; always feature branch + PR.
- Don't commit, push, open PRs, or merge on your own initiative — each step happens only when Jeremy asks. Completing one step is not permission to chain into the next.
- Only merge when Jeremy directs it. Avoid `gh pr merge --auto` here: main has no required status checks, so `--auto` merges immediately instead of waiting for CI.
- No `Co-Authored-By` lines, no "Generated with Claude Code" footer, no Test-plan checklists in PR bodies.
- Don't bump versions or tag releases unless explicitly asked.

## Naming

- Default to **KernSmith** everywhere casing is a free choice: prose, headings, link text, UI display copy, PackageId, namespaces, assembly names.
- Use lowercase **Kernsmith** only inside a `github.com/kaltinril/…` URL path, or where another external identifier is genuinely spelled that way.
- Markdown link *text* still uses the brand: `[KernSmith](https://github.com/kaltinril/Kernsmith)`.
- Never mass-lowercase brand or display text to "match" the repo name.

## Testing & Verification

- Always TDD: write the test, run it against unmodified code, confirm it fails for the expected reason, then fix.
- For bug fixes, prove the test catches the bug — stash the fix, watch it go red, restore, watch it go green. A test only ever seen green is unverified.
- Any change to a rasterizer backend, atlas packing, or other pixel-output code needs `python tests/bmfont-compare/regression_check.py` before the work is done — not just xUnit green. Run it proactively.
- If no existing `.bmfc` exercises the option you changed, ADD one — otherwise the harness reports "identical" for reasons unrelated to correctness.
- Use `git add -f` for shared `tests/bmfont-compare/` configs and confirm they appear in `git status`.
- Use a generic font available everywhere (Georgia, Arial) in shared `.bmfc` configs, never a machine-specific Gum font.
- CliTests need a Debug build — use plain `dotnet test`, not `-c Release --no-build`.

## CodeQL & Alerts

- Verify PR CodeQL results on `refs/pull/N/merge`, never `/head`.
- Keep dismissal comments under 280 characters, and surface API stderr rather than swallowing it.
- Do not re-add a `paths-ignore` block to the CodeQL config — it is inert for C# here.
- Do not dismiss the 4 rasterizer float-equality alerts without asking.

## Working Style

- Direct, no-fluff answers. Jeremy will push back on assertions that aren't argued — give the reasoning, not the conclusion alone.
- Stay in scope: review/validate/doc requests do not authorize implementation.
- Debug then ask: trace the path once and form a hypothesis; if the cause isn't clear, ask what Jeremy observes rather than re-running static analysis.
- Surface decisions in chat with context and a recommendation before asking; don't bury the question in a tool batch.
- Prefers thorough manual testing via `.bat` files with timing output; test output to a gitignored `output/` folder.
- Report actual generation time vs startup overhead — performance is a first-class concern.
- Parallel agents for independent tasks.
- Read the relevant README before guessing run/build/publish commands.

## Context Management

- NEVER read large doc/plan files in the main context window — delegate to agents.
- Multi-file edits go to coder agents; main context is for orchestration.
- Batch doc updates into a single agent call.
