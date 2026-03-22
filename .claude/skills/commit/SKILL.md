---
name: commit
description: "Commit staged and unstaged changes with an auto-drafted message. Handles the full git workflow: status check, diff analysis, message drafting, selective staging (skips data/, .env, credentials), and post-commit verification. Use this whenever the user says 'commit', 'commit changes', 'commit this', or any variation of wanting to save their work to git."
argument-hint: "[optional message override]"
---

# Commit

Automate the full git commit workflow in one shot. No user intervention needed between steps unless something looks wrong.

## Important

Run all git commands directly (e.g., `git status`) without prefixing `cd <path> &&`. The Bash tool already runs in the project's working directory, and the `cd` prefix breaks permission allow-list matching on `Bash(git *)`.

## Steps

### 1. Gather context (run all three in parallel)

```bash
git status           # overview of staged, unstaged, untracked (NEVER use -uall)
git diff --stat      # summary of staged + unstaged changes
git log --oneline -5 # recent messages for style matching
```

### 2. Check for phase doc updates

Before drafting the commit message, scan the diff for changes that should be reflected in project docs. If changes touch phase deliverables (new API methods, schema changes, parser updates, etc.), check whether `plan/master-plan.md` or the relevant `plan/phase-*.md` file needs updating. If so, make those edits and include them in the commit. Don't create a separate commit for doc updates — bundle them together.

### 2b. Update phase document status

If the changes being committed implement work from a specific phase, update the phase's status in both the phase document and the master plan. This keeps the project's planning docs in sync with reality.

**How to determine which phase is affected:**
- Look at the files being changed and match them to phase deliverables
- Check commit message context (e.g., "Phase 55" in the message or branch name)
- If unsure, skip this step — only update when you're confident

**What to update in the phase doc** (`plan/phase-*.md`):
- Update the `> **Status**:` line in the header (e.g., `Planning` → `In Progress`, `In Progress` → `Complete`, or `Planning` → `Partial` if only some items were done)

**What to update in** `plan/master-plan.md`:
- Update the status column in the **Active Plans** table to match
- Update the phase heading in the **Phased Implementation** section (e.g., `(PLANNING)` → `(IN PROGRESS)` or `(COMPLETE)`)

**Status values:**
- `Planning` — no implementation started
- `In Progress` — implementation underway
- `Partial` — some deliverables complete, others deferred
- `Complete` — all deliverables done

Stage the updated plan files alongside the code changes so everything ships in one commit.

### 3. Analyze and draft the commit message

Read the diff output and recent commit history, then draft a message that:

- Matches the repo's existing commit style (this project uses imperative, descriptive first lines like `Phase 30: Search & Discovery — 5 search pages with filters, grids, URL sync`)
- Summarizes the **why**, not just the what
- Keeps the first line under ~72 characters when possible; use a blank line + body for detail
- For multi-file changes, mention the scope (e.g., "Phase 30:", "Fix:", "Add:")

### 4. Stage files selectively

Add relevant files by name. **Never use `git add -A` or `git add .`** — list files explicitly.

**Always skip** (do not stage):
- `data/` directory
- `.env`, `.env.*` files
- `credentials.yml`, `credentials.json`, or similar secrets
- Large binary files unless the user explicitly included them

If `$ARGUMENTS` is provided and starts with `-m`, use the text after `-m` as the commit message override instead of auto-drafting.

### 5. Commit

Use a HEREDOC for the message to preserve formatting. Do NOT append `Co-Authored-By` or any attribution trailers — the commit message should contain only the descriptive message.

```bash
git commit -m "$(cat <<'EOF'
<first line>

<optional body>
EOF
)"
```

### 6. Verify

Run `git status` after the commit to confirm it succeeded and the working tree is clean (or shows only the expected untracked files).

## Edge cases

- **Nothing to commit**: If `git status` shows no changes, say so and stop. Do not create an empty commit.
- **Pre-commit hook failure**: If the commit fails due to a hook, investigate and fix the issue, then create a **new** commit (never `--amend`, which would modify the previous commit).
- **Sensitive files in changes**: If you spot `.env`, credentials, or secrets in the diff, warn the user and exclude them from staging.
- **User provides `-m` argument**: Use their message verbatim as the commit message.
