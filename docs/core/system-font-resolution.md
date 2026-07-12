# System Font Resolution

`BmFont.GenerateFromSystem()` resolves a family name (e.g. `"Arial"`) to an actual font file on
disk through a four-tier fallback chain, implemented in `KernSmith.Font.DefaultSystemFontProvider`.
Each tier is cheaper than the next, so the common case — a family already resolved once, or a
well-known one — never pays for the expensive tiers at all.

This page documents the chain end-to-end. It matters most on **macOS**: Windows has a registry
fast path, and Linux has `fc-list`, but macOS has neither, so it's the one platform where every
tier — including the expensive ones — routinely gets exercised in practice.

## The fallback chain

For a given family name, tiers run in this order until one produces a result:

1. **Cache / hint / seed** — a hit in the in-memory resolved-font cache, either from:
   - a prior successful resolution for this family (any tier), or
   - a consumer-supplied hint via [`BmFont.HintFontLocation()`](../api-reference/bmfont.md#font-location-hints), or
   - a lazy, one-shot lookup against the built-in [seed table](#the-seed-table) the first time
     the family is requested.

   Every candidate here — cached, hinted, or seeded — is validated identically before being
   trusted (see [validate-before-trust](#validate-before-trust)). No directory enumeration or
   parsing of any other font happens if this tier hits.

2. **Windows registry** (`HKLM`/`HKCU` `...\Fonts`) — fast, Windows-only, a no-op on macOS/Linux.
   If the registry confirms the family exists but not the requested style (e.g. "Arial" exists
   but "Arial Black Italic" doesn't), resolution stops here and returns "not found" rather than
   falling through — there's no separate file for that style to find elsewhere.

3. **Heuristic filename match** — see [below](#the-heuristic-tier). Narrows candidates by
   filename first (cheap directory enumeration, no parsing), then verifies with a real parse.

4. **Full directory scan** — the correctness backstop. Every font under the platform's font
   directories (macOS: `/System/Library/Fonts`, `/Library/Fonts`, `~/Library/Fonts`) is
   enumerated and parsed, and family/style are matched against the real embedded names. This
   always eventually finds a real, installed font regardless of whether the earlier tiers'
   guesses were right — it's just the slowest way to get there.

A successful resolution from tier 2, 3, or 4 is written back into the cache, so the next
lookup for that family (with no style requested) is a pure tier-1 hit.

## Validate-before-trust

Every entry that can satisfy tier 1 — a cache hit, a consumer hint, or a seed guess — goes
through the same check before it's used: the file must exist, and parsing it must produce a
font whose real embedded family name matches the requested family. Nothing is ever trusted
just because a filename or path looks right.

This means a wrong guess is harmless: it costs exactly one bounded file-exists check (and, if
the file does exist, one parse), then resolution falls through to the next tier as if the bad
entry had never been there. This is what makes it safe to keep a table of best-guess paths
(the seed table) or accept an unverified hint from a consumer.

## The seed table

`WellKnownFontSeeds` (internal) is a fixed, KernSmith-maintained table of best-guess file paths
for common font families, keyed by platform:

- **Windows** — filenames relative to the Windows font directories (e.g. `"Arial" → "arial.ttf"`).
- **macOS** — absolute paths (e.g. `"Arial" → "/System/Library/Fonts/Supplemental/Arial.ttf"`,
  with a fallback to the older `/Library/Fonts/Arial.ttf` location).
- **Linux** — no table. `fc-list` (used by the full-scan tier's fast path) already covers common
  families quickly enough that seeding would add nothing.

On macOS, five entries — Segoe UI, Tahoma, Calibri, Cambria, Consolas — are Microsoft fonts not
bundled with stock macOS; they're only present if Microsoft Office is installed, typically under
`/Library/Fonts/Microsoft/`. Those paths (and the exact Arial supplemental-vs-legacy split) are
the lowest-confidence entries in the table — they haven't been verified against real Office
installs across versions. A miss here is exactly as harmless as any other seed miss (see
[validate-before-trust](#validate-before-trust)): it just falls through to the heuristic and
full-scan tiers.

Because the seed table can never cause an incorrect resolution — only a slower one when it
guesses wrong — it doesn't need to be authoritative for every OS version or locale. If you know
your app needs a family not in this table (or the table's guess is wrong on your target OS
version), don't wait for KernSmith to update it — call
[`BmFont.HintFontLocation()`](../api-reference/bmfont.md#font-location-hints) with the real path
yourself.

## The heuristic tier

Before paying for a full directory scan, the heuristic tier narrows candidates cheaply:

1. Enumerate filenames only (no parsing) across the platform's font directories.
2. Normalize both the requested family name and each filename (lowercase, strip spaces/hyphens/
   underscores) and keep files whose normalized name *contains* the normalized family name.
3. Parse only those narrowed candidates, and check the real embedded family name against the
   request.

Step 3 exists specifically to avoid a classic false positive: a request for `"Helvetica"` would
filename-match `HelveticaNeue-Bold.ttf` in step 2 (its normalized name contains
`"helvetica"`), but that file's actual embedded family is `"Helvetica Neue"`, not
`"Helvetica"` — so step 3 rejects it. A filename match is only ever a hint; the parsed name is
the source of truth.

If step 2 finds no filename-narrowed candidates at all, this tier falls through to the full
scan (there might still be a real match with a totally unrelated filename). If step 2 finds
candidates but every one of them fails step 3's real-name check, that's treated as a
**definitive miss** — resolution does not escalate to the full scan, since a full scan against
the same font set would only re-discover the same false positives.

## Diagnostic messages

Every tier miss is logged via `Trace.TraceInformation`, so you can find out — without profiling
— which family names on your users' machines are paying for the expensive tiers. Attach a
`System.Diagnostics.TraceListener` to see them (e.g. `Trace.Listeners.Add(...)`, or route them
through your app's existing logging via a custom listener).

| Message contains | Tier | Meaning | What to do |
|---|---|---|---|
| `cached/hinted font entry for '<family>' ... is no longer valid` | 1 | A previously-cached resolution or a hint you supplied no longer validates (file moved/deleted, or its contents changed to a different family). | If this is your own hint, check the path is still correct for the target machine. |
| `seed candidate for '<family>' at '<path>' was invalid` | 1 | A built-in seed guess didn't pan out on this machine (file missing, or wrong family at that path). | Usually nothing to do — this is expected on OS versions/locales the seed table didn't anticipate. If it's a family you use heavily, consider a hint instead. |
| `no filename-narrowed candidates found for '<family>'` | 3 | Nothing in the font directories had a filename resembling the family. | Falling through to a full scan is correct here; if this family is hit often, add a hint to skip tiers 2-4 entirely. |
| `filename-narrowed candidate(s) for '<family>' failed real verification` | 3 | Filenames looked plausible but none actually contained that family (the "Helvetica" vs. "Helvetica Neue" case). | Same as above — a hint with the exact correct path avoids this tier being tried at all. |
| `'<family>' requires a full font directory scan` | 4 | **The single most useful message** — this family paid the full cost of enumerating and parsing every installed font. | The strongest signal to act on: call `BmFont.HintFontLocation()` (if you know the path) or `BmFont.RegisterFont()` (to bundle/embed the font) for this family. |

## Warming up resolution

If you know in advance where one of your fonts lives — or your font ships under a family name
the heuristic tier won't filename-match — you don't have to wait for KernSmith to discover it
the slow way. See [Font location hints](../api-reference/bmfont.md#font-location-hints) for
`BmFont.HintFontLocation()`, or [Font registration](../api-reference/bmfont.md#font-registration)
for `BmFont.RegisterFont()` when you'd rather embed the font's bytes directly (required on
platforms with no filesystem access at all, like Blazor WASM).
