# KernSmith.DeterminismProbe

A console app that generates cases of the Native determinism matrix and compares their hashes with the committed manifest. It is the proof that the Native backend produces the same bytes under NativeAOT (and, through `wasm-check.mjs`, in the browser) as under the JIT: the matrix (`DeterminismMatrix.cs`) and the hash (`DeterminismHash.cs`) are compiled in from `tests/KernSmith.Rasterizers.Native.Tests/Determinism`, so the test project, this probe and the WASM sample agree by construction. The manifest is `tests/KernSmith.Rasterizers.Native.Tests/Determinism/manifest.json`: the SHA-256 of the `.fnt` text and raw atlas pixels per case (PNG file bytes are not part of it; deflate follows the runtime's zlib).

See `TESTING-NATIVE.md` at the repository root for the whole determinism check, and the "Same bytes on every host" section of `src/KernSmith.Rasterizers.Native/README.md` for what is promised and why it holds.

## Run under the JIT

```
dotnet run --project tests/KernSmith.DeterminismProbe -f net10.0 -- --fixtures tests/KernSmith.Tests/Fixtures --manifest tests/KernSmith.Rasterizers.Native.Tests/Determinism/manifest.json
```

## Run as a NativeAOT binary

```
dotnet publish tests/KernSmith.DeterminismProbe -c Release -r win-x64 -p:PublishAot=true -o out/aot
out/aot/KernSmith.DeterminismProbe --fixtures tests/KernSmith.Tests/Fixtures --manifest tests/KernSmith.Rasterizers.Native.Tests/Determinism/manifest.json
```

Use `-r linux-x64` or `-r osx-arm64` on those hosts (NativeAOT needs the platform's C toolchain: `clang` and `zlib1g-dev` on Debian/Ubuntu). The first line of the output names the runtime and says `dynamic code unsupported (AOT)` when the binary really is AOT-compiled; CI greps for it.

## Arguments

| Argument | Meaning |
|----------|---------|
| `--fixtures <dir>` | Directory with the fixture fonts (`tests/KernSmith.Tests/Fixtures`). Required unless `--list` |
| `--manifest <manifest.json>` | The committed manifest to compare against. Required unless `--list` |
| `--case <id prefix>` | Only cases whose id starts with the prefix (repeatable), e.g. `--case roboto/32px/ --case bungee/32px/grayscale/default/color`. Default: every case |
| `--list` | Print every case id and exit |

Each case prints `OK`, `MISMATCH` (expected and actual hash, plus a short hash per part -- `.fnt` text and each page's pixels -- so you can see which part differs), `MISSING` (not in the manifest) or `ERROR`. Exit code 0 when every selected case matches, 1 on any mismatch, missing case or generation error, 2 on a usage or setup problem.

## Browser leg: `wasm-check.mjs`

Serves a published Blazor WASM sample (`samples/KernSmith.Samples.BlazorWasm`) from a local HTTP server, opens it in headless Chromium with Playwright, reads the determinism hash the page reports and compares it with the manifest:

```
(cd tests/KernSmith.DeterminismProbe && npm install && npx playwright install chromium)
node tests/KernSmith.DeterminismProbe/wasm-check.mjs --dir out/wasm/wwwroot --manifest tests/KernSmith.Rasterizers.Native.Tests/Determinism/manifest.json [--case roboto/32px/grayscale/default] [--timeout 300] [--channel chrome]
```

`--channel chrome` uses an installed Google Chrome instead of Playwright's Chromium. Same exit codes as the probe.
