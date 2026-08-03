# Project Rules

How to work on this project. One short rule per line. These OVERRIDE default behavior.

Git workflow, TDD, the regression harness, context management and security live in [CLAUDE.md](CLAUDE.md) because they must fire before anyone opens this file. They are not repeated here.

## Naming

- Default to **KernSmith** everywhere casing is a free choice: prose, headings, link text, UI display copy, PackageId, namespaces, assembly names.
- Use lowercase **Kernsmith** only inside a `github.com/kaltinril/…` URL path, or where another external identifier is genuinely spelled that way.
- Markdown link *text* still uses the brand: `[KernSmith](https://github.com/kaltinril/Kernsmith)`.
- Never mass-lowercase brand or display text to "match" the repo name.

## Writing Code

- Follow existing patterns — check 2-3 nearby files before writing new code.
- Read `plan/done/plan-data-types.md` before adding or changing types and interfaces.
- Never hardcode credentials — use environment variables or `.env` plus an appropriate library.
- Test with real data; the fixture font is at `tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf`.
- Never add a `PackageVersion` property to a csproj — the version lives only in `Directory.Build.props`.
- Comments carry INVARIANTS the code cannot show: load-bearing constraints, keep-in-sync obligations, why a dead-looking entry stays, measured "don't undo this" facts.
- Comments are NOT chronicles: no dated readings, no "was X, became Y", no phase-supersession trails, no reviewer-facing justification. State the constraint in the present tense and stop.
- Read code, not git history, when debugging.
- Search actual identifiers, not guesses.
- Use WebFetch, not curl, for web research.

## Testing

- Every fixed bug earns a permanent regression test so it can never silently return.
- A test that cannot fail is worse than no test: a soft-skip or a check that reports PASS without actually running is indistinguishable from real coverage.
- Verify a gate FAILS, not just that it passes — reintroduce the condition, confirm the non-zero exit, then revert.
- Run the full `dotnet test`, not a filtered subset, when touching TTF parsing, atlas packing, or a rasterizer backend; a whole class of input can break while the tests you happened to run stay green.
- Assertions use Shouldly; never write `.Should()`. Two gotchas: string `ShouldContain`/`ShouldNotContain` default to **case-insensitive**, so pass `Case.Sensitive` explicitly on string receivers; and `ShouldBe(…, ignoreOrder: true)` compares with `Equals`, so reference types without value equality need `ShouldBeEquivalentTo`.

## Documentation

- Don't document unsupported or scaffold features in user-facing docs or the CLI until they actually work. Once a feature does work but is incomplete, document it as clearly-labeled experimental rather than hiding it.
- Internal `reference/REF-*` docs and code XML comments may describe a clearly-labeled scaffold for contributors.
- Docs are three-layered: per-package READMEs (where backends and satellite packages are documented), hand-written docfx guides under `docs/`, and the docfx-generated API reference — which covers only the core `src/KernSmith` project and is gitignored (`/api/` and `/_site/` are build artifacts).
- Don't expand `docfx.json` metadata to satellite packages without a reason.
- When auditing docs, verify every factual claim — especially "included / built-in / bundled" wording — against the real package structure, and scan `docs/` too, not just root and `plan/`.
- Don't sprinkle counts or status numbers through docs. Record a number only when it is load-bearing (a hard API limit, a measured performance delta), never as an inventory that will drift.
- Keep XML doc-comments on the public API accurate — `src/KernSmith` sets `GenerateDocumentationFile`, so they render into the published API reference and a stale "not yet implemented" ships to the site.
- Add the change to the `[Unreleased]` section of `CHANGELOG.md` in the same PR.
- A phase status change updates the phase table in `plan/master-plan.md` in the same commit.
- A completed phase doc moves to `plan/done/` in the same commit, fixing relative links in the moved doc and in every referrer.
- Phase docs are lean specs, not logs or journals: status is ONE line, replaced rather than appended; no dated annotations, no commit SHAs, no "was X, now Y", no strikethrough. State the current truth and delete what it replaced.
- Keep `.claude/skills/` and `.claude/agents/` current when paths or conventions change.

## CodeQL & Alerts

- Verify PR CodeQL results on `refs/pull/N/merge`, never `/head`.
- Keep dismissal comments under 280 characters, and surface API stderr rather than swallowing it.
- Do not re-add a `paths-ignore` block to the CodeQL config — it is inert for C# here.
- Do not dismiss the rasterizer float-equality alerts without asking.

## Working Style

- Direct, no-fluff answers. Jeremy pushes back on assertions that aren't argued — give the reasoning, not the conclusion alone.
- Stay in scope: review/validate/doc requests don't authorize implementation; don't let prior-turn momentum widen a bounded task.
- Debug then ask: trace the path once and form a hypothesis; if the cause isn't clear, ask what Jeremy observes rather than re-running static analysis. Scope debug agents narrowly (one file, one theory).
- Surface decisions in chat with context and a recommendation in plain text before asking; don't bury the question inside a tool batch.
- Verify before asserting. A claim copied forward without re-checking is how stale facts get enshrined.
- Prefers thorough manual testing via `.bat` files with timing output; test output to a gitignored `output/` folder.
- Report actual generation time vs startup overhead — performance is a first-class concern.
- Parallel agents for independent tasks.
- Read the relevant README before guessing run/build/publish commands.
- Answer questions before making edits; a research question is not a build request, and waiting for the answer beats guessing.
- Jeremy's statements are fact: act on them rather than re-verifying. Only if something observed contradicts one, say so.
- Don't overthink — match effort to task size; prefer a direct fix over an elaborate workflow.
- When picking up unfamiliar work, read `git log --oneline -20`, the `[Unreleased]` section of `CHANGELOG.md`, and the phase status rows in `plan/master-plan.md` before assuming the state of anything.

## Maintaining these three files

The next session starts with CLAUDE.md and these three files and nothing else. Anything known only to a finished conversation is lost work.

- `project_facts.md`, `project_rules.md` and `project_decisions.md` are the only home for durable facts, rules and decisions. Do not create memory files.
- Update them in the same commit as the change that alters them.
- Edit in place and delete entries that become false; never append changelogs or dated progress notes, since history lives in git.
- When a conversation surfaces a new durable fact, a correction to behavior, or a resolved choice, write it to the right file immediately without being asked.
- Facts state what is true; rules state how to work; decisions state what was chosen and why. An entry with no rejected alternative and no reason is a fact, not a decision.
- Record only what reading the code cannot tell you. Folder layout, file names and format constants are derivable and belong in the code or the docs.
- Verify a claim before carrying it forward. A fact copied from a previous session without re-checking is how stale information gets enshrined as truth.
