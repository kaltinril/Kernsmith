# Phase 35 — FontStashSharp Rasterizer Plugin

> **Status**: Rejected
> **Created**: 2026-03-30
> **Rejected**: 2026-04-01
> **Reason**: FontStashSharp is a wrapper over stbTrueTypeSharp, which KernSmith already uses directly via Phase 32. No value in adding an indirect dependency.

See Phase 32 for the StbTrueType rasterizer implementation. Useful techniques from this research were distilled into Phases 160-180 for the pure C# native rasterizer effort.
