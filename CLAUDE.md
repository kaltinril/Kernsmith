# [Project Name]

## Project Purpose

<!-- 1-2 sentence description of what this project does -->

## Project Organization

| Folder | Purpose |
|--------|---------|
| `src/` | **Main application source code** |
| `tests/` | **Test suites** |
| `docs/` | **Documentation** |

<!-- Add/remove rows as needed for your project structure -->

## Context Management

- **NEVER read large doc/plan files in the main context window.** Delegate to agents.
- **Multi-file edits MUST go to coder agents.** Main context is for orchestration only.
- **Batch doc updates into a single agent call.**
- **Why**: Reading 6+ large markdown files inline causes context compaction.

## Agent Instructions

### When Working on This Project

1. **Read project docs first** before making changes
2. **Follow existing patterns** — check 2-3 nearby files before writing new code
3. **Never hardcode credentials** — use environment variables or `.env` + appropriate library
4. **Test with real data** when available

### Key Conventions

- **Language**: <!-- e.g., Python 3.12, TypeScript 5.x, etc. -->
- **Database**: <!-- e.g., PostgreSQL, MySQL, SQLite, etc. -->
- **Config**: `.env` file, never commit `.env` to git
- **Logging**: Use the language's standard logging library
- **Testing**: <!-- e.g., pytest, jest, xUnit, etc. -->

### Keeping Skills & Agents Current

When changes affect counts, file paths, or conventions referenced by skills or agents, update those files too:

- **`.claude/skills/`**: Skills may embed test counts, file paths, and project patterns. When these change, scan skill SKILL.md files for stale values.
- **`.claude/agents/`**: Agents are pattern-based and rarely drift, but review if conventions change significantly.
- **Use `/update-docs`**: This skill audits both documentation AND skills for stale values.

### Project File References

| What | Location |
|------|----------|
| Main application | `src/` |
| Tests | `tests/` |
| Documentation | `docs/` |
| CI/CD | `.github/workflows/` |

<!-- Add rows for key files and directories in your project -->
