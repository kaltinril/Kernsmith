# Test-Font Licenses

This file documents the licenses and upstream sources of the **font files committed
to this repository for testing**. These fonts are used only by the KernSmith test
suite and comparison harness.

> **These fonts are test fixtures.** They are **not** redistributed in any published
> KernSmith NuGet package or application binary. They live in the repository solely
> so the automated tests and the BMFont comparison harness have real font data to
> run against. They are not part of the KernSmith product.

Each font below is freely licensed (Apache-2.0 or SIL Open Font License 1.1) and may
be redistributed in source form, which is why committing them to a public repository
is permitted. Copyright lines are reproduced verbatim from each font's upstream
metadata / license file.

---

## Fonts

### Roboto-Regular.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf`
- **Family:** Roboto
- **Foundry / Source:** Google
- **License (SPDX):** `Apache-2.0` (classic Roboto) — see note below
- **Project / Source URL:** https://github.com/google/roboto
- **Copyright:** `Copyright 2011 Google Inc. All Rights Reserved.` (classic Roboto,
  Apache-2.0)

> **Which Roboto license applies?** The long-standing **"classic" Roboto** (the
> Roboto 2.x family historically shipped as `Roboto-Regular.ttf` in game-dev /
> BMFont contexts, and published at https://github.com/google/roboto) is licensed
> under the **Apache License 2.0**. In 2024 Google re-released **Roboto 3.x** on
> Google Fonts under the **SIL Open Font License 1.1**
> (https://github.com/googlefonts/roboto-3-classic). The committed file is treated
> here as the classic **Apache-2.0** Roboto. **[UNCONFIRMED]** — the exact build of
> the committed `Roboto-Regular.ttf` was not verified against its embedded `name`
> table during this pass, so if it is in fact a Roboto 3.x build, **OFL-1.1** would
> apply instead. Both licenses permit committing the font to a public repository and
> using it for testing.

### NotoColorEmoji.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/NotoColorEmoji.ttf`
- **Family:** Noto Color Emoji
- **Foundry / Source:** Google (Noto project)
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1)
- **Project / Source URL:** https://github.com/googlefonts/noto-emoji
- **Copyright (verbatim):** `Copyright 2013 Google LLC`

### RobotoFlex-Variable.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/RobotoFlex-Variable.ttf`
- **Family:** Roboto Flex (variable font)
- **Foundry / Source:** Google
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1)
- **Project / Source URL:** https://github.com/googlefonts/roboto-flex
- **Copyright (verbatim):**
  `Copyright 2011 The Roboto Flex Project Authors (https://github.com/googlefonts/roboto-flex)`

### SourceSerif4-Regular.otf

- **Path:** `tests/KernSmith.Tests/Fixtures/SourceSerif4-Regular.otf`
- **Family:** Source Serif 4 (static, CFF / PostScript outlines)
- **Foundry / Source:** Adobe
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1) — full text in
  `SourceSerif4-OFL.txt` next to the font
- **Project / Source URL:** https://github.com/adobe-fonts/source-serif (`release` branch, `OTF/`)
- **Copyright (verbatim):**
  `Copyright 2014 - 2023 Adobe (http://www.adobe.com/), with Reserved Font Name ‘Source’. All Rights Reserved. Source is a trademark of Adobe in the United States and/or other countries.`

### SourceCodeVF-Upright.otf

- **Path:** `tests/KernSmith.Tests/Fixtures/SourceCodeVF-Upright.otf`
- **Family:** Source Code Pro (variable font, CFF2 outlines)
- **Foundry / Source:** Adobe
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1) — full text in
  `SourceCodeVF-OFL.txt` next to the font
- **Project / Source URL:** https://github.com/adobe-fonts/source-code-pro (`release` branch, `VF/`)
- **Copyright (verbatim):**
  `© 2023 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source'. All Rights Reserved. Source is a trademark of Adobe in the United States and/or other countries.`

### NotoSans-Regular-Latin.ttf / NotoSans-Regular-Latin.woff2

- **Path:** `tests/KernSmith.Tests/Fixtures/NotoSans-Regular-Latin.{ttf,woff2}`
- **Family:** Noto Sans (hinted, Latin subset)
- **Foundry / Source:** Google (Noto project). Source: hinted `NotoSans-Regular.ttf` v2.015 from
  https://github.com/notofonts/notofonts.github.io (`fonts/NotoSans/hinted/ttf/`, commit `28b15b4b`)
- **License (SPDX):** `OFL-1.1` — full text in `OFL-NotoSans.txt`; no Reserved Font Name
- **Copyright (verbatim):** `Copyright 2022 The Noto Project Authors (https://github.com/notofonts/latin-greek-cyrillic)`
- **Derived with fontTools 4.68:**
  `fonttools subset NotoSans-Regular.ttf --unicodes="U+0020-007E,U+00A0-00FF,U+2018-201F" --layout-features='*' --notdef-outline --output-file=NotoSans-Regular-Latin.ttf`,
  then `fonttools ttLib.woff2 compress --hmtx-transform NotoSans-Regular-Latin.ttf`.

### NotoSans-Regular-Latin-CFF.otf / NotoSans-Regular-Latin-CFF.woff2

- **Path:** `tests/KernSmith.Tests/Fixtures/NotoSans-Regular-Latin-CFF.{otf,woff2}`
- **Family:** Noto Sans (unhinted CFF build, Latin subset)
- **Foundry / Source:** Google (Noto project). Source: unhinted `NotoSans-Regular.otf` from
  https://github.com/notofonts/notofonts.github.io (`fonts/NotoSans/unhinted/otf/`, commit `28b15b4b`)
- **License (SPDX):** `OFL-1.1` — full text in `OFL-NotoSans.txt`; no Reserved Font Name (which is
  why it, not a Source font, is the CFF WOFF2 fixture: OFL counts a format change as a Modified
  Version, and a Modified Version may not keep a Reserved Font Name)
- **Copyright (verbatim):** `Copyright 2022 The Noto Project Authors (https://github.com/notofonts/latin-greek-cyrillic)`
- **Derived with fontTools 4.68:**
  `fonttools subset NotoSans-Regular.otf --unicodes="U+0020-007E,U+00A0-00FF,U+2018-201F" --layout-features='*' --notdef-outline --output-file=NotoSans-Regular-Latin-CFF.otf`,
  then `fonttools ttLib.woff2 compress NotoSans-Regular-Latin-CFF.otf`.

### RobotoFlex-Latin.ttf / RobotoFlex-Latin.woff2

- **Path:** `tests/KernSmith.Tests/Fixtures/RobotoFlex-Latin.{ttf,woff2}`
- **Family:** Roboto Flex (default instance, Latin subset)
- **Source:** the committed `RobotoFlex-Variable.ttf` above
- **License (SPDX):** `OFL-1.1` — full text in `OFL-RobotoFlex.txt`; no Reserved Font Name
- **Copyright (verbatim):** `Copyright 2011 The Roboto Flex Project Authors (https://github.com/googlefonts/roboto-flex)`
- **Derived with fontTools 4.68:** `fontTools.varLib.instancer.instantiateVariableFont(font, {every axis: None})`
  (pin every axis at its default; keeps the OVERLAP_SIMPLE glyph flags), then
  `fonttools subset --unicodes="U+0020-007E,U+00C0-00FF" --layout-features='*' --notdef-outline`,
  then `fonttools ttLib.woff2 compress --hmtx-transform RobotoFlex-Latin.ttf`.

### Roboto-Regular.woff2

- **Path:** `tests/KernSmith.Tests/Fixtures/Roboto-Regular.woff2`
- **Source / license:** the committed `Roboto-Regular.ttf` above (same license), converted with
  `fonttools ttLib.woff2 compress Roboto-Regular.ttf` (fontTools 4.68; glyf transform, hmtx stored as-is).

### DancingScript-Variable.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/DancingScript-Variable.ttf` (the same file is also
  used, untracked, from `tests/bmfont-compare/gum-bmfont/` by the comparison harness)
- **Family:** Dancing Script (variable font)
- **Foundry / Source:** Google / Pablo Impallari (Impallari Type)
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1)
- **Project / Source URL:** https://github.com/googlefonts/DancingScript (originally
  https://github.com/impallari/DancingScript)
- **Copyright (verbatim):**
  `Copyright 2016 The Dancing Script Project Authors (https://github.com/googlefonts/DancingScript), with Reserved Font Name 'Dancing Script'.`

### Color/BungeeColor-Regular.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/Color/BungeeColor-Regular.ttf` (license text: `Color/BungeeColor-OFL.txt`)
- **Family:** Bungee Color (`COLR` version 0 + `CPAL` color font)
- **Foundry / Source:** David Jonathan Ross, via Google Fonts (`ofl/bungeecolor`)
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1)
- **Project / Source URL:** https://github.com/djrrb/Bungee
- **Copyright (verbatim):** `Copyright 2008 The Bungee Project Authors (david@djr.com)`

### Color/ReemKufiInk-Regular.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/Color/ReemKufiInk-Regular.ttf` (license text: `Color/ReemKufiInk-OFL.txt`)
- **Family:** Reem Kufi Ink (`COLR` version 1 color font)
- **Foundry / Source:** Khaled Hosny, Santiago Orozco, via Google Fonts (`ofl/reemkufiink`)
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1)
- **Project / Source URL:** https://github.com/aliftype/reem-kufi
- **Copyright (verbatim):**
  `Copyright 2015-2022 The Reem Kufi Project Authors (https://github.com/aliftype/reem-kufi), with Reserved Font Name "Josefin Sans".`

> The color fixtures live in the `Color/` subfolder on purpose: `ColorFontTests` uses the
> first color font found directly in `Fixtures/` and its tests expect Noto Color Emoji.

---

## SIL Open Font License 1.1 — reservation note

The OFL-1.1 fonts above carry **Reserved Font Names** (e.g. *"Dancing Script"*, *"Josefin Sans"*). Per
the OFL, a derivative or modified version of an OFL font **must not** use any
Reserved Font Name of the original. The font files in this repository are used
**unmodified**, for testing only, and are not redistributed under a different name — except the
Noto Sans and Roboto Flex subsets above, which derive from fonts that declare no Reserved Font Name.

The SIL Open Font License, Version 1.1 (26 February 2007) preamble, reproduced
verbatim:

```
PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.
```

The complete SIL OFL-1.1 text and FAQ are available at:
https://openfontlicense.org (formerly http://scripts.sil.org/OFL). Each OFL font
above ships the full license text in its own `OFL.txt` at its upstream source URL.

The Apache License 2.0 (for classic Roboto) is available at:
https://www.apache.org/licenses/LICENSE-2.0
