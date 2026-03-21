# Phase 05 — Full CLI Tool

> **Status**: Complete
> **Original doc**: plan-phase-future.md
> **Date**: 2026-03-19

---

Production-ready CLI with 5 commands (generate, inspect, convert, list-fonts, info), .bmfc config file support, and full option coverage.

See **[plan-cli.md](plan-cli.md)** for full specification and task breakdown.

## Completed Deferred Items from Phase 3

Items originally deferred from Phase 3 (Ecosystem) that were completed during this phase:

| Original ID | Task | Description | Notes |
|-------------|------|-------------|-------|
| 13B | **Color font support** | COLRv0/CPAL, sbix, CBDT via FT_LOAD_COLOR + RGBA atlas | **DONE** -- Phases A-C implemented. 20 tests + 4 skip-ready. Plan: [plan-color-fonts.md](plan-color-fonts.md). |
| 13C | **Font subsetting** | Logical subsetting -- filter cmap/kern/GPOS during parsing | **DONE** -- 22 tests. Plan: [plan-font-subsetting.md](plan-font-subsetting.md). |
| 15C | **NuGet publishing CI** | Configure CI for NuGet pack + push, README, package icon | **DONE** -- publish.yml updated, .csproj metadata added, README created. |
| 16C | **Tests: CLI** | End-to-end CLI invocation tests | **DONE** -- 20 tests in `tests/KernSmith.Tests/Cli/CliTests.cs`. |
